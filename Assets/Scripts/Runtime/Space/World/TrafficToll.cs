// TrafficToll.cs
// Traffic: Loma's pirate toll (part of the partial Traffic; its fields are in Traffic.cs).

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
        /// <summary>A yes / no question in flight through the HUD's ChoiceWindow (the Loma toll's; TerritoryView's toll).</summary>
        public void Ask(string text, string yes, string no, Action onYes, Action onNo) => ChoiceRequested?.Invoke(text, yes, no, onYes, onNo);
        /// <summary>Remake: the Mining Plant has sorted space waste (WasteProcessing): the HUD's result page (PauseMenu);
        /// without one, the answer as a message. 'closed' once it is dismissed.</summary>
        public event Action<WasteProcessing.Sorting, Database, Action> WasteSorted;
        public void ShowSorting(WasteProcessing.Sorting sorting, Database db, Action closed)
        {
            if (WasteSorted != null) WasteSorted(sorting, db, closed);
            else Ask(WasteProcessing.Result(db, sorting), null, null, closed, null);
        }
        /// <summary>Remake: a menu of every option at once through the HUD's question window (the Mining Plant's,
        /// PauseMenu.Choose): its sections (headings, lines, item rows), each entry's text as shown and its action; Back
        /// takes the last entry.</summary>
        public event Action<string, List<MenuSection>, List<(string label, Action action)>> MenuRequested;
        public void ShowMenu(string title, List<MenuSection> sections, List<(string label, Action action)> entries) =>
            MenuRequested?.Invoke(title, sections, entries);
        /// <summary>Modified: a question with a title, its answers side by side (the Supernova Challenge's end, like the
        /// original's ChoiceWindow 1.2: "Supernova Challenge" over the text, Yes and No in a row). ChoiceTitle is set while
        /// ChoiceRequested runs.</summary>
        public void Ask(string title, string text, string yes, string no, Action onYes, Action onNo)
        {
            ChoiceTitle = title;
            try { ChoiceRequested?.Invoke(text, yes, no, onYes, onNo); }
            finally { ChoiceTitle = null; }
        }
        /// <summary>The toll is paid: Loma's pirates are neither hostile nor friendly (PlayerFighter::update).</summary>
        public bool LomaTollPaid => blackMarket && Session.LomaTollPaid;
        /// <summary>MGame::OnInitialize: outside system 25 both flags clear. In Loma the pirates hold fire (KIPlayer+0x21 = 0)
        /// unless the toll was refused; a new visitor hears radio 9 (449 / 450) and is asked afterwards, one who refused
        /// radio 0xd (455 / 456).</summary>
        void SetupLoma()
        {
            if (!blackMarket) { Session.LomaTollPaid = Session.LomaTollRefused = false; return; }
            if (Session.LomaTollRefused) { Radio(455, 456, Standing.Pirate); return; }
            SetPiratesArmed(false);
            if (Session.LomaTollPaid) return;
            Radio(449, 450, Standing.Pirate);
            lomaAskPending = true;
        }
        void SetPiratesArmed(bool on)
        {
            foreach (var s in Ships) if (s.Race == Standing.Pirate) s.shootingEnabled = on;
        }
        /// <summary>MGame::OnUpdate: once the arrival radio is over, the toll 448: the cargo's value (amount x single price,
        /// 100 when empty) x 2 / 5 / 10 / 20 % by difficulty (0 / 0.5 / 1.0 / Extreme); #P = the percentage.</summary>
        void UpdateLomaToll()
        {
            if (!lomaAskPending || chatter != null || chatterQueue.Count > 0 || Player == null || !Player.Alive) return;
            lomaAskPending = false;
            int value = 0;
            int priceStation = Session.PreviousStationIndex >= 0 ? Session.PreviousStationIndex : StationIndex;
            var items = Session.Cargo.ConvertAll(c => c.item);
            var prices = Shop.PriceList(db, priceStation, items);
            for (int i = 0; i < items.Count; i++) value += prices[i] * Session.Cargo[i].amount;
            if (value == 0) value = 100;
            float d = Session.Difficulty;
            float f = d <= 0f ? 0.02f : d == 0.5f ? 0.05f : d == 1f ? 0.1f : 0.2f;
            lomaPercent = Mathf.RoundToInt(f * 100f);
            lomaToll = (int)(f * value);
            string text = Localization.Get(448).Replace("#C", GoF2Remake.UI.ItemInfo.Credits(lomaToll)).Replace("#P", lomaPercent.ToString());
            ChoiceRequested?.Invoke(text, Localization.Get(134), Localization.Get(135), PayLomaToll, RefuseLomaToll);
        }
        /// <summary>Yes: with the credits, pay (radio 10: 451 / 452), the pirates stay quiet; without, 203 and they attack.</summary>
        void PayLomaToll()
        {
            if (Session.Credits < lomaToll)
            {
                ChoiceRequested?.Invoke(Localization.Get(203).Replace("#C", GoF2Remake.UI.ItemInfo.Credits(lomaToll - Session.Credits)), null, null, null, null);
                RefuseLomaToll();
                return;
            }
            Session.Credits -= lomaToll;
            Session.LomaTollPaid = true;
            SetPiratesArmed(false);
            Radio(451, 452, Standing.Pirate);
        }
        /// <summary>No (or too poor): the pirates arm, the refusal is remembered (+0x111), radio 0xb (453 / 454).</summary>
        void RefuseLomaToll()
        {
            SetPiratesArmed(true);
            Session.LomaTollRefused = true;
            Radio(453, 454, Standing.Pirate);
        }
        /// <summary>Player::damage on a pirate in Loma: Level::alarmAllFriends(8) (radio 0xc, 453), every pirate armed, the
        /// toll revoked.</summary>
        public void LomaPirateHit()
        {
            if (!blackMarket) return;
            lomaAskPending = false;
            AlarmAllFriends(Standing.Pirate, true);
            SetPiratesArmed(true);
            Session.LomaTollRefused = true;
            Session.LomaTollPaid = false;
        }
        /// <summary>SET_FOG_STATIC (space_props.md 4): 30 sprites of 32768 units within +-40000 of the outpost, colour
        /// 0xE2282880, forever, not tied to the camera. Level::initParticleSystems adds it to the fog manager (Level+0x7c):
        /// material 20095 fog.png, additive, like the ambient fog; no HDR glow (the original blends in LDR).</summary>
        void SpawnRedFog(Vector3 at)
        {
            var go = new GameObject("Pirate base fog");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 30;
            main.startLifetime = float.PositiveInfinity;
            main.startSpeed = 0f;
            main.startSize = 32768f * 0.05f;
            main.startColor = new Color(0xE2 / 255f, 0x28 / 255f, 0x28 / 255f, 0x80 / 255f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.maxParticleSize = 10f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (assets != null && assets.fogMaterial != null)
            {
                var fog = new Material(assets.fogMaterial) { name = "Pirate base fog" };
                if (fog.HasProperty("_Glow")) fog.SetFloat("_Glow", 1f);
                r.sharedMaterial = fog;
            }
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < 30; i++)
            {
                p.position = at + new Vector3(UnityEngine.Random.Range(-40000f, 40000f), UnityEngine.Random.Range(-40000f, 40000f), UnityEngine.Random.Range(-40000f, 40000f)) * 0.05f;
                p.rotation = UnityEngine.Random.Range(0f, 360f);
                ps.Emit(p, 1);
            }
        }
        /// <summary>The Terran battleship died: its turrets go with it.</summary>
        public void DestroyTurrets()
        {
            foreach (var s in Ships.ToArray()) s.DestroyAsTurret();
        }
        /// <summary>Remake (CapitalShips' fleet battles): a line of the race's radio (a generic face, like Radio) with a literal
        /// text, queued after the waiting ones.</summary>
        public void RaceRadio(string text, int race)
        {
            if (RadioBlocked || string.IsNullOrEmpty(text)) return;
            int image = race == 0 ? 64 : race == 2 ? 65 : race == 3 ? 21 : race == 8 ? 9 : 63;
            Say(new Chatter { text = text, speaker = Localization.Get(1597 + image),
                              portrait = AgentGenerator.CreatePortrait(true, race == 0 || race == 2 || race == 3 ? race : 1) });
        }
        /// <summary>Remake (CapitalShips): a bounty paid in flight (the HUD's bounty message).</summary>
        public void PayBounty(int reward)
        {
            Session.Credits += reward;
            BountyCollected?.Invoke(reward);
        }
        /// <summary>Remake (CapitalShips): a fleet battle's two capital ships both still stand.</summary>
        public bool FleetBattleRaging
        {
            get
            {
                int n = 0;
                foreach (var s in Ships) if (s.Spec.fleetBattle && !s.Gone && s.Current == NpcShip.State.Fly && s.Target.Alive) n++;
                return n >= 2;
            }
        }
        /// <summary>Remake (CapitalShips): a killable carrier / Vossk battleship died: its own turrets go with it.</summary>
        public void DestroyCapitalTurrets(SpawnSpec host)
        {
            foreach (var s in Ships.ToArray()) if (s.Spec.capitalHost == host) s.DestroyAsTurret();
        }
        /// <summary>createRadioMessage kinds 0 / 1 say nothing at one of step 59's target stations (Status+0x90).</summary>
        bool AtStoryTarget => !Session.FreePlay && Session.StoryTargets != null && Session.StoryTargets.Contains(StationIndex);
        /// <summary>Level::friendTurnedEnemy: radio 0 once per level.</summary>
        public void FriendTurnedEnemy(int race)
        {
            if (radioTurned) return;
            radioTurned = true;
            if (!AtStoryTarget) Radio(426, 428, race);
        }
        /// <summary>Level::alarmAllFriends: every ship of 'race' turns hostile; radio 1 once; the station remembers it.</summary>
        public void AlarmAllFriends(int race, bool radio)
        {
            foreach (var s in Ships) if (s.Race == race) s.alwaysEnemy = true;
            if (radio && !radioAlarm)
            {
                radioAlarm = true;
                if (blackMarket) Radio(453, 453, race);   // kind 0xc in the black market
                else if (!AtStoryTarget) Radio(429, 431, race);
                if (race == SystemRace) Session.AttackedStations.Add(StationIndex);
            }
        }
        public void OnShipDied(NpcShip ship, bool byPlayer)
        {
            ShipDied?.Invoke(ship, byPlayer);
            // Informer mission (PlayerFighter::update ~0xf1cb0): the spy dead -> Status+0xf0; the player shooting another
            // ship in its orbit -> Status+0xf1 (failed on the next docking).
            var fm = Session.FreelanceMission;
            if (fm != null && fm.type == MissionType.Informer && fm.target == StationIndex)
            {
                // PlayerFighter::update 0xf1c8c: any other ship dying (whoever killed it) spoils it (+0xf1 = 1, +0xf0 = 0),
                // and after that the spy's death no longer counts. The original checks only +0xf1, so a death after the
                // spy's fails it too; the remake's default stops counting once the spy is dead (Settings.InformerOriginalRule).
                bool killed = Session.InformerKilled, failed = Session.InformerFailed;
                if (ship.Spec.nameText == 1663) { if (!Session.InformerFailed) Session.InformerKilled = true; }
                // Remake (a player's report): the pirate events' ships never count (the original's Informer orbit has none; TrafficPlan
                // skips them there, but a multiplayer orbit another player built may hold them).
                else if (ship.Spec.pirateEvent == 0 && !Session.InformerFailed && (!Session.InformerKilled || Settings.InformerOriginalRule))
                {
                    Session.InformerFailed = true;
                    Session.InformerKilled = false;
                }
                // Multiplayer: the squad's mission (NetMissions.OnStatus: 1 = the spy dead, 1000 = spoiled).
                if (Session.InformerKilled && !killed) GoF2Remake.World.SpaceHooks.MissionStatus(fm, 1);
                if (Session.InformerFailed && !failed) GoF2Remake.World.SpaceHooks.MissionStatus(fm, 1000);
            }
            // PlayerFighter::update's death: a Most Wanted criminal pays its bounty whoever killed it; no standing hit.
            if (ship.Spec.wantedIndex >= 0) { WantedKilled(ship); if (byPlayer && ship.Target.hostileToPlayer) CountKill(); return; }
            if (ship.Spec.pirateEvent == SpawnSpec.EventOutpost || ship.Spec.pirateEvent == SpawnSpec.EventBoss) PirateEventDone(ship);   // remake: the event's bounty, whoever killed it
            if (!byPlayer) return;
            // Player::damage: the convoy freighter ("Arms delivery") destroyed by the Liberator (0xb3) -> step 59's bonus.
            if (ship.Spec.convoyRole == SpawnSpec.ConvoyFreighter && ship.Target.lastPlayerWeapon == 179 && Session.StoryMission != null)
                Session.StoryMission.value++;
            // Player::damage: no standing change for a kill in the black market (Loma); the kill still counts
            // (Level::enemyDied's incKills, PlayerFighter::update's incPirateKills).
            if (!blackMarket) Standing.ApplyKill(ship.Race, SystemRace);
            if (ship.Target.hostileToPlayer)
            {
                if (PlayerHealth.EmergencyActive)
                {
                    Session.GraveRiserKills = Mathf.Max(Session.GraveRiserKills, ++emergencyKills);
                    Achievements.Progress(43, Session.GraveRiserKills);   // Level::enemyDied: Status+0x13c
                }
                else emergencyKills = 0;
                CountKill();
                if (ship.Race == Standing.Pirate) Session.PirateKills++;
            }
        }
        /// <summary>Level::enemyDied: Status::incKills; without a scanner also Status+0x11c (medal 40 Blindfolded Killer).</summary>
        void CountKill()
        {
            Session.Kills++;
            if (!hasScanner && !Achievements.Has(40)) Achievements.Elite(40, ++Session.BlindKills);
        }
    }
}
