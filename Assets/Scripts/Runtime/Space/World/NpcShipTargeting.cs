// NpcShipTargeting.cs
// Part of NpcShip: relations (hostile / friend flags), sleeping and waking, and target selection: the original's
// rules, the cloak pursuit and the remake's smart targeting (NpcTargetRules).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        /// <summary>§4.2: hostile / friend flags for the markers and the AI.</summary>
        void UpdateRelations()
        {
            int r = Race;
            bool alwaysHostile = r == Standing.Pirate || r == Standing.Void || r == Standing.Specter;
            bool hostile = alwaysHostile || Standing.IsEnemy(r);
            bool friend = !alwaysHostile && Standing.IsFriend(r);
            // PlayerFighter::update's order: always-enemy, then Loma's paid toll (pirates neither hostile nor friendly), then a
            // turned ship, and the always-friend flag last (setAlwaysEnemy doesn't clear it: a story ally stays one).
            if (alwaysEnemy) { hostile = true; friend = false; }
            if (alwaysNeutral) { hostile = false; friend = false; }
            if (r == Standing.Pirate && traffic != null && traffic.LomaTollPaid) { hostile = false; friend = false; }
            if (turnedEnemy) { hostile = true; friend = false; }
            if (HostileToLocalBySquad != null && HostileToLocalBySquad(this)) { hostile = true; friend = false; }   // multiplayer
            int territory = TerritoryToLocal != null ? TerritoryToLocal(this) : 0;   // multiplayer: a faction station's defence
            if (territory > 0 && !turnedEnemy) { hostile = false; friend = true; }
            else if (territory < 0) { hostile = true; friend = false; }
            if (alwaysFriend) { hostile = false; friend = true; }
            Target.hostileToPlayer = hostile;
            Target.friendToPlayer = friend;
        }

        bool InBox(Target t)
        {
            var d = t.transform.position - transform.position;
            float r = (detectRange > 0f ? detectRange : NpcTables.DetectRange) * M;
            return Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r;
        }

        static bool Valid(Target t) => t != null && t.Targetable;

        /// <summary>State 5 (sleeping): a hostile sleeper wakes when the player (or the steered Liberator) comes within its
        /// detect range per axis (+-50 000 by default, not while the player is cloaked) or within +-25 000 (cloaked too);
        /// other ships never wake it. A non-hostile sleeper wakes when its target is within the detect range; fixed objects
        /// and freighters on any enemy within +-50 000. Hostile sleepers stay hidden after the tutorial (PlayerFighter,
        /// index &gt; 1). An inactive ship only wakes by script.</summary>
        void UpdateSleep()
        {
            bool hide = Hidden;
            if (modelGo != null && modelGo.activeSelf == hide) modelGo.SetActive(!hide);
            Target.untargetable = true;
            if (inactive) return;
            bool Near(Vector3 p, float units) { var d = p - transform.position; float r = units * M; return Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r; }
            // Freighters and fixed objects are PlayerFixedObjects: PlayerFixedObject::update (0x17f480) state 5 wakes them on
            // the first active enemy within +-50 000 per axis (a constant: no detect range, and no cloak test, only
            // Player::isActive).
            if (IsFixed || IsFreighter)
            {
                foreach (var e in enemies)
                    if (Valid(e) && Near(e.transform.position, NpcTables.DetectRange)) { Wake(); return; }
                return;
            }
            // PlayerFighter::update: KIPlayer+0x124 is the detect range (50 000 by default; 0 = never by proximity).
            float dr = detectRange >= 0f ? detectRange : NpcTables.DetectRange;
            if (dr <= 0f) return;
            var sleepTarget = SleepTarget(dr);
            if (Target.hostileToPlayer)
            {
                // 0xf19b8: a hostile sleeper (Player+0x5c) has its target delta (+0x16c) overwritten by the player's position,
                // or the steered rocket's (PlayerEgo::isInRocketControl), whatever its target is: within +-25 000 per axis it
                // wakes at once (no cloak test), and state 5 (0xf2750) wakes it within the detect range unless its target
                // (+0x144, the player unless another ship was picked) is cloaked (+0x5e). Other ships never wake it.
                var player = traffic != null ? traffic.Player : null;
                Vector3? at = WeaponSystem.GuidedRocket ?? (player != null ? player.transform.position : (Vector3?)null);
                if (at.HasValue && (Near(at.Value, 25000f) || (sleepTarget != null && !sleepTarget.cloaked && Near(at.Value, dr)))) { Wake(); return; }
                // Multiplayer: another player it is hostile to wakes it the same way.
                var remote = RemotePlayers;
                if (remote != null && HostileToRemote != null)
                    foreach (var r in remote)
                    {
                        if (r == null || !r.Alive || !r.isActiveAndEnabled || !HostileToRemote(this, r)) continue;
                        if (Near(r.transform.position, 25000f) || (!r.cloaked && Near(r.transform.position, dr))) { Wake(); return; }
                    }
            }
            // State 5: the delta to the target itself, within the detect range (not for a cloaked target).
            else if (sleepTarget != null && !sleepTarget.cloaked && Near(sleepTarget.transform.position, dr)) { Wake(); return; }
        }

        /// <summary>PlayerFighter+0x144 while asleep: the TARGETING (§5.3) keeps running in state 5. The first enemy in the
        /// box; a non-hostile ship's pick of the player moves on to index 1, and any pick but the player becomes the first
        /// race-hostile ship at any range; with none (the sleeper is inactive, so the route branch fails Player::isActive)
        /// enemy 0, the player. The 5 s re-roll's random picks are left out.</summary>
        Target SleepTarget(float dr)
        {
            int n = enemies.Count, idx = -1;
            bool anyRange = TakesAtAnyRange(Race, turnedEnemy);
            float r = dr * M;
            for (int i = 0; i < n; i++)
            {
                var e = enemies[i];
                if (!Valid(e)) continue;
                var d = e.transform.position - transform.position;
                if (anyRange || (Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r)) { idx = i; break; }
            }
            if (!Target.hostileToPlayer && idx == 0) idx = 1;
            if (idx > 0)
            {
                idx = -1;
                for (int i = 1; i < n; i++)
                    if (Valid(enemies[i]) && Standing.RacesHostile(Race, enemies[i].race)) { idx = i; break; }
            }
            return idx >= 0 ? enemies[idx] : n > 0 ? enemies[0] : null;
        }

        /// <summary>KIPlayer vtable +0x0c: awake (visible, flying, attacking).</summary>
        public void Wake()
        {
            if (Asleep && Spec.guard) traffic?.PirateStationAction(true);   // a pirate base's guard woke
            Asleep = inactive = false;
            Target.untargetable = false;
            if (modelGo != null && Current == State.Fly) modelGo.SetActive(!forcedHidden);
        }

        /// <summary>Remake option (Settings.CloakLosesPursuers, off = the original, which keeps chasing a cloaked target
        /// and only holds its fire): a cloaked target is lost. The ship flies to where it was last seen, then back to its
        /// route.</summary>
        static bool LostToCloak(Target t) => t != null && t.cloaked && Settings.CloakLosesPursuers;   // a session: the host's (NetRules)
        static bool Pursuable(Target t) => Valid(t) && !LostToCloak(t);

        /// <summary>PlayerFighter::update 0xf131e / 0xf1444: a turned ship (Player::turnedEnemy, +0xe0) of any race but the
        /// pirates' (8) takes a target outside its detection box too, when it picks one (not attacking, or the 5 s re-roll's
        /// random tries). The re-roll still drops a pick outside the box, so it flies its route until the next one.</summary>
        internal static bool TakesAtAnyRange(int race, bool turned) => turned && race != Standing.Pirate;

        /// <summary>§5.3 target selection.</summary>
        void UpdateTargeting()
        {
            if (attacking && target != null && LostToCloak(target)) { lastSeen = target.transform.position; searchMs = SearchMs; }
            if (searchMs > 0f)
            {
                searchMs -= Time.deltaTime * 1000f;
                if ((lastSeen - transform.position).sqrMagnitude < SearchReach * M * SearchReach * M) searchMs = 0f;
            }
            // Multiplayer: another player it is hostile to, kept while valid, hostile and in the box.
            if (remoteTarget != null)
            {
                if (Pursuable(remoteTarget) && HostileToRemote != null && HostileToRemote(this, remoteTarget) && InBox(remoteTarget))
                {
                    target = remoteTarget;
                    targetPos = target.transform.position;
                    attacking = true;
                    followingWaypoint = false;
                    return;
                }
                remoteTarget = null;
            }
            UpdateTargetingLocal();
            if (attacking && target != null) return;
            var remote = RemotePlayers;
            if (remote == null || HostileToRemote == null) return;
            foreach (var r in remote)
            {
                if (!Pursuable(r) || !HostileToRemote(this, r) || !InBox(r)) continue;
                remoteTarget = r;
                target = r;
                targetPos = r.transform.position;
                attacking = true;
                followingWaypoint = false;
                return;
            }
        }

        void UpdateTargetingLocal()
        {
            int n = enemies.Count;
            int idx = targetIdx;
            if (idx >= n) idx = -1;
            if (!attacking) idx = -1;
            else if (idx >= 0 && !Pursuable(enemies[idx])) attacking = false;
            if (SmartTargeting && UpdateSmartTargeting(n, idx)) return;
            bool anyRange = TakesAtAnyRange(Race, turnedEnemy);
            if (reselectTimer < 5001f)
            {
                if (!attacking)
                    for (int i = 0; i < n; i++)
                        if (Pursuable(enemies[i]) && (anyRange || InBox(enemies[i]))) { idx = i; attacking = true; break; }
            }
            else
            {
                drift = !drift && Random.Range(0, 100) < 20;
                reselectTimer = 0f;
                if (Random.Range(0, 100) < 30 && n > 1)
                {
                    attacking = false;
                    for (int k = 0; k < 5; k++)
                    {
                        int i = Random.Range(0, n);
                        if (Pursuable(enemies[i]) && (anyRange || InBox(enemies[i]))) { idx = i; attacking = true; break; }
                    }
                    if (!attacking) idx = 0;
                }
                else idx = 0;
                // The 5 s re-roll: a target outside the box is dropped (a turned ship too: it flies its route until the next one).
                if (n > 0 && Pursuable(enemies[idx])) { if (!InBox(enemies[idx])) idx = -1; }
                else { idx = -1; attacking = false; }
            }
            if (!Target.hostileToPlayer && idx == 0) { idx = 1; attacking = false; }
            if (idx > 0)
            {
                idx = -1;
                // Remake (CapitalShips): a fleet battle's fighter takes the nearest race-hostile ship (its hull's nearest
                // point), not the first of the list: in a Terran system the local patrols come first, so the Vossk side's
                // fighters flew off to them and never touched the enemy capital ship.
                bool nearest = Spec.capitalPart && Spec.capitalHost != null && Spec.capitalHost.fleetBattle;
                // ... and only within the battle's area, so the fighters stay between and around the capital ships.
                Vector3 centre = Vector3.zero;
                float reach = 0f;
                bool leash = nearest && CapitalShip.BattleArea(Spec.capitalHost, out centre, out reach);
                // Remake (performance, 2026-10-22): the nearest pick is held for NearestHoldMs while it can still be taken,
                // then made again: every fighter of a fleet battle scanned every enemy's hull every frame (20 fleet battles,
                // ~600 fighters: 20 ms of an 80 ms frame).
                nearestHoldMs -= Time.deltaTime * 1000f;
                int held = targetIdx;
                if (nearest && nearestHoldMs > 0f && held > 0 && held < n && (object)enemies[held] == heldNearest && Pursuable(heldNearest)
                    && Standing.RacesHostile(Race, heldNearest.race)
                    && (!leash || (heldNearest.transform.position - centre).sqrMagnitude <= reach * reach))
                { idx = held; attacking = true; }
                else
                {
                    float best = float.MaxValue;
                    var at = transform.position;
                    int frame = Time.frameCount;
                    for (int i = 1; i < n; i++)
                    {
                        var e = enemies[i];
                        if ((object)e == null || !Standing.RacesHostile(Race, e.race)) continue;
                        if (!nearest) { if (!Pursuable(e)) continue; idx = i; attacking = true; break; }
                        // The cheap tests first, from the target's sphere (Target.BroadSphere, measured once a frame): the
                        // leash on its centre, and none of it is nearer than its centre less its reach, so a ship that can't
                        // beat the best so far needs neither the engine's checks (Pursuable) nor its hull's nearest point.
                        float r = e.BroadSphere(frame, out var c);
                        if (r < 0f) continue;   // destroyed
                        if (leash && (c - centre).sqrMagnitude > reach * reach) continue;
                        float bound = Mathf.Max(0f, (c - at).magnitude - r);
                        if (bound * bound >= best || !Pursuable(e)) continue;
                        float d = (e.NearestPoint(at) - at).sqrMagnitude;
                        if (d < best) { best = d; idx = i; attacking = true; }
                    }
                    if (nearest) { heldNearest = idx > 0 ? enemies[idx] : null; nearestHoldMs = Random.Range(NearestHoldMs * 0.5f, NearestHoldMs); }
                }
            }
            targetIdx = idx;
            followingWaypoint = false;
            target = null;
            if (idx < 0 || idx >= n)
            {
                if (searchMs > 0f) { attacking = false; targetPos = lastSeen; followingWaypoint = true; return; }   // the option
                var wp = route.Waypoint;
                // A finished route: the target is the player (PlayerFighter+0x144), so the break-off circle applies, but it isn't
                // an attack: no firing.
                if (wp == null && LostToCloak(traffic.Player))   // the option: no circling a cloaked player, straight on
                { attacking = false; targetPos = transform.position + transform.forward * (20000f * M); followingWaypoint = true; }
                else if (wp == null) { target = traffic.Player; attacking = false; targetPos = traffic.Player != null ? traffic.Player.transform.position : transform.position; }
                else { route.Update(ToGame(transform.position)); wp = route.Waypoint; targetPos = wp.HasValue ? ToUnity(wp.Value) : transform.position; followingWaypoint = true; }
            }
            else { target = enemies[idx]; targetPos = target.transform.position; }
        }

        /// <summary>A target the smart pick may take: pursuable, and the player's side (the player, its sentry guns) only while
        /// this ship is hostile to the player, any other ship only when its race is hostile to this one's.</summary>
        bool SmartEligible(Target e)
        {
            if (!Pursuable(e)) return false;
            return PlayerSide(e) ? Target.hostileToPlayer : Standing.RacesHostile(Race, e.race);
        }

        static bool PlayerSide(Target e) => e.isPlayer || (e.friendToPlayer && e.GetComponent<SentryGun>() != null);

        /// <summary>The smart pick; false = nothing in the box, the original's rules then (its routes, its far pick).</summary>
        bool UpdateSmartTargeting(int n, int idx)
        {
            bool reroll = reselectTimer >= 5001f;
            // Between the 5 s re-rolls a target is kept while it can still be taken (the original keeps its pick too).
            int pick = !reroll && attacking && idx >= 0 && SmartEligible(enemies[idx]) ? idx : SmartPick(n);
            if (pick < 0) return false;
            if (reroll) { drift = !drift && Random.Range(0, 100) < 20; reselectTimer = 0f; }
            targetIdx = pick;
            attacking = true;
            followingWaypoint = false;
            target = enemies[pick];
            targetPos = target.transform.position;
            return true;
        }

        int SmartPick(int n)
        {
            smartCandidates.Clear();
            smartIndices.Clear();
            var at = transform.position;
            for (int i = 0; i < n; i++)
            {
                var e = enemies[i];
                if (!SmartEligible(e) || !InBox(e)) continue;
                smartCandidates.Add(new NpcTargetRules.Candidate
                {
                    sqrDistance = (e.NearestPoint(at) - at).sqrMagnitude,
                    player = PlayerSide(e),
                });
                smartIndices.Add(i);
            }
            if (smartCandidates.Count == 0) return -1;
            if (smartCandidates.Count > 1)   // how many others attack each (one candidate is the pick anyway)
                for (int c = 0; c < smartCandidates.Count; c++)
                {
                    var e = enemies[smartIndices[c]];
                    int count = 0;
                    foreach (var s in traffic.Ships)
                        if (s != this && s.attacking && s.target == e && s.Current == State.Fly) count++;
                    var cand = smartCandidates[c];
                    cand.attackers = count;
                    smartCandidates[c] = cand;
                }
            int k = NpcTargetRules.Pick(smartCandidates, NpcTargetRules.AttackerCap(Session.Difficulty));
            return k >= 0 ? smartIndices[k] : -1;
        }
    }
}
