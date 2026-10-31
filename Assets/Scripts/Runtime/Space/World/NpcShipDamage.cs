// NpcShipDamage.cs
// Part of NpcShip: hits and damage (gun hits, blasts, friendly fire, EMP, other players' hits in multiplayer), death
// (dying, the wreck, the explosion), loot and crates, and cargo stealing.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        /// <summary>Multiplayer: another player's hit (NetProxy). Like the player's friendly fire (OnDamaged), it turns on
        /// that player's squad once they took half its hull (a quarter on Extreme); a race that is always hostile needs none.
        /// The hull damage itself comes in as an NPC's hit (no standing or kill credit for this game's player).</summary>
        public void OnRemoteHit(ulong client, int dmg)
        {
            if (alwaysEnemy || IsWingman || Race == Standing.Pirate || Race == Standing.Void || Race == Standing.Specter) return;
            // Remake (CapitalShips): a capital ship, its turrets and escorts turn on another player's squad at the first hit.
            if (Spec.capitalEnhanced && (Spec.capital != 0 || Spec.capitalPart)) { aggressors.Add(client); return; }
            int total = (remoteHullDamage.TryGetValue(client, out int d) ? d : 0) + dmg;
            remoteHullDamage[client] = total;
            if (total >= Hp.maxHull * (Session.IsExtreme ? 0.25f : 0.50f)) aggressors.Add(client);
        }

        /// <summary>Multiplayer: another player's EMP (NetProxy): past a third of its EMP points it turns on them
        /// (OnPlayerEmp), then the EMP lands.</summary>
        public void OnRemoteEmp(ulong client, int emp)
        {
            if (Target.Alive) BreakCloak();
            if (Hp == null || !Target.Alive || Hp.emp <= 0) return;
            if (!alwaysEnemy && !IsWingman && Race != Standing.Void && Race != Standing.Specter)
            {
                int total = (remoteEmp.TryGetValue(client, out int e) ? e : 0) + emp;
                remoteEmp[client] = total;
                if (total > Hp.maxEmp / 3) aggressors.Add(client);
            }
            Hp.DamageEmp(emp);
        }

        /// <summary>PlayerFighter::PlayerFighter: race 9 (the Void) gets no cargo list (KIPlayer+0x4c = 0), so the scanner
        /// reads "Nothing to salvage." and there is nothing to steal; its 1-3 t Alien Remains are made at death (OnDied).
        /// Level::createShip 0xcf83c deletes the cargo list of ships 44 (Specter) and 49 (Scimitar), and PlayerFighter::revive
        /// gives the Specters (race 10) none: they never leave a container (the remake's first life rolled loot until 2026-10).
        /// The others: Generator::getLootList.</summary>
        List<ItemStack> RollLoot() => Spec.race == Standing.Void || Spec.race == Standing.Specter || Spec.ship == 44 || Spec.ship == 49
            ? new List<ItemStack>() : NpcTables.RollLoot(db, Spec.freighter);

        /// <summary>KIPlayer::setDead: inactive until relaunched.</summary>
        void SetDead()
        {
            cloak?.Stop();
            Current = State.Dead;
            rig?.HideAll();
            empRig?.HideAll();
            secondRig?.HideAll();
            mounted?.HideAll();
            smoking = false;
            smoke?.Clear(); sparks?.Clear(); burn?.SetBurning(false);
            if (wreck != null) Destroy(wreck);
            gameObject.SetActive(false);
        }

        /// <summary>Player::setHitpoints(0) + KIPlayer::setDead (a level script): gone at once, without the tumble, the
        /// explosion, a crate or the kill bookkeeping.</summary>
        public void Vanish()
        {
            Hp.hull = 0;
            Target.hp = 0;
            if (engine != null) engine.Stop();
            SetDead();
        }

        // ---- combat ----------------------------------------------------------------------------------------

        void OnGunHit(int bullet, Target hit, Vector3 point) => GunHit(gun, rig, bullet, hit, point);

        /// <summary>A gun's hit and, for a gun with an echo (GunEcho), its blasts: the echo's area hurts what it reaches.
        /// Guns without an echo keep their plain hits, as before.</summary>
        void WireGun(Gun g)
        {
            g.Hit += OnGunHit;
            if (g.echo != null) g.AreaHit += HitByBlast;
        }

        /// <summary>An NPC gun's area blast on one target (Gun.AreaDamage, the echo): the hull damage and EMP, no stray-fire
        /// rules (the blast is what the gun is for). The player takes no area damage (Gun.AreaDamage skips it).</summary>
        public void HitByBlast(Target t, int dmg, int emp, Vector3 center)
        {
            if (t == null) return;
            if (dmg > 0) t.Damage(dmg, true, (t.transform.position - center).normalized);
            if (emp > 0 && !t.isPlayer && t.hitpoints != null) t.hitpoints.DamageEmp(emp);
        }

        static bool Contains(IReadOnlyList<Target> list, Target t)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == t) return true;
            return false;
        }

        /// <summary>Gun::calcCharacterCollision 0x17e154 for an NPC bullet: a non-hostile ship's stray shot at the player does
        /// 20 %, a hostile one x0.75 while the player is docked at an object (PlayerEgo::isDockedToDockingPoint); the item's
        /// EMP (attr 10, Gun::setIndex) lands too (NPC against NPC: the player has no EMP pool).</summary>
        void GunHit(Gun g, GunRig r, int bullet, Target hit, Vector3 point)
        {
            float dmg = g.damage;
            if (hit.isPlayer && !Target.hostileToPlayer) dmg = (int)(dmg * 0.2f);   // stray fire from a non-hostile ship
            else if (!hit.isPlayer && RemotePlayers != null && HostileToRemote != null && Contains(RemotePlayers, hit) && !HostileToRemote(this, hit))
                dmg = (int)(dmg * 0.2f);   // multiplayer: the same for another player it isn't after
            else if (hit.isPlayer && ObjectDocking.PlayerDocked) dmg = (int)(dmg * 0.75f);
            else if (!hit.isPlayer && RemoteDockedAtObject != null && RemoteDockedAtObject(hit)) dmg = (int)(dmg * 0.75f);   // multiplayer
            hit.Damage(dmg, true, g.bullets[bullet].velocity);
            if (g.emp > 0f) EmpBreaksCloak(hit);
            if (g.emp > 0f && !hit.isPlayer && hit.hitpoints != null) hit.hitpoints.DamageEmp((int)g.emp);
            r.ShowImpact(point);
        }

        /// <summary>Remake (Settings.EmpBreaksCloak): an EMP hit drops this ship's cloak (NpcCloak.Break).</summary>
        public void BreakCloak()
        {
            if (cloak != null && Settings.EmpBreaksCloak) cloak.Break();
        }

        /// <summary>An NPC's EMP shot on 'hit': its cloak drops, the player's or another NPC's (Settings.EmpBreaksCloak).</summary>
        static void EmpBreaksCloak(Target hit)
        {
            if (hit == null || !Settings.EmpBreaksCloak) return;
            if (hit.isPlayer) PlayerCloak.Of(hit)?.Break();
            else hit.GetComponent<NpcShip>()?.BreakCloak();
        }

        /// <summary>Player::damage friendly-fire bookkeeping (§4.5), hits by the player only.</summary>
        void OnDamaged(Target t, int dmg, bool byNpc)
        {
            // Player::damage on a Most Wanted criminal (+0x3e): Level::attackWanted instead of the friendly-fire rules.
            if (!byNpc && Spec.wantedIndex >= 0) { traffic.AttackWanted(this, dmg); return; }
            // Player::damage 0xafd80: hitting a pirate in Loma arms and alarms them all and revokes the toll.
            if (!byNpc && Race == Standing.Pirate) traffic.LomaPirateHit();
            if (byNpc || alwaysEnemy || noFriendlyFire || IsWingman || Race == Standing.Void || Race == Standing.Specter) return;
            if (Target.hostileToPlayer && !turnedEnemy) return;
            if (Race != traffic.SystemRace && Race != traffic.AttackRace) return;
            damageByPlayer += dmg;
            bool hc = Session.IsExtreme;
            float max = Hp.maxHull;
            // Player::damage: a signature of this race, or any of races 0..3 at the higher threshold, becomes invalid.
            int sig = Standing.SignatureRace;
            if (sig >= 0 && Race <= 3 && ((Race == sig && damageByPlayer >= max * (hc ? 0.10f : 0.33f)) || damageByPlayer >= max * (hc ? 0.25f : 0.50f))
                && Standing.InvalidateSignature())
                traffic.Warn(Localization.Get(324));
            if (damageByPlayer > max * (hc ? 0.10f : 0.33f)) traffic.FriendTurnedEnemy(Race);
            if (damageByPlayer >= max * (hc ? 0.25f : 0.50f)) turnedEnemy = true;
            if (damageByPlayer >= max * (hc ? 0.40f : 0.66f)) traffic.AlarmAllFriends(Race, true);
        }

        /// <summary>Player::damageEmp by the player, before the EMP lands: a Wanted criminal counts as attacked; a ship of the
        /// system's race (not a wingman, the Void, a Specter or an always-enemy) past a third of its EMP points turns on the
        /// player ("Hold your fire!", Level::friendTurnedEnemy).</summary>
        public void OnPlayerEmp(int emp)
        {
            if (alwaysEnemy) return;
            if (Race != Standing.Void && Race != Standing.Specter && Spec.wantedIndex >= 0) { traffic.AttackWanted(this, 0); return; }
            if (IsWingman || Race == Standing.Void || Race == Standing.Specter || Race != traffic.SystemRace) return;
            empByPlayer += emp;
            if (empByPlayer > Hp.maxEmp / 3) { turnedEnemy = true; traffic.FriendTurnedEnemy(Race); }
        }

        /// <summary>Player::damageEmp's disable: the system's race all turn hostile (alarmAllFriends(race, false), no radio);
        /// Standing::applyDisable unless it is a Wanted criminal.</summary>
        public void OnPlayerDisabled()
        {
            if (!alwaysEnemy && Race != Standing.Void && Race != Standing.Specter && Race == traffic.SystemRace)
                traffic.AlarmAllFriends(Race, false);
            if (Spec.wantedIndex < 0 && (Race >= 0 && Race <= 3 || Modding.ModRaces.Is(Race))) Standing.ApplyDelict(Race, 2);
        }

        /// <summary>Hull &lt; 1: the dying state (§5.10) / the freighter wreck (§6).</summary>
        void OnDied(Target t)
        {
            cloak?.Stop();
            Target.cloaked = false;
            // PlayerFighter's death: a hostile Void ship leaves 1-3 t Alien Remains (131).
            if (Race == Standing.Void && !Spec.noLoot && Target.hostileToPlayer && !loot.Exists(s => s.item == 131))
                loot = new List<ItemStack> { new ItemStack(131, Random.Range(0, 3) + 1) };
            traffic.OnShipDied(this, !Target.killedByNpc);
            // Remake mods: a ship the player destroys may drop a mod blueprint (blueprints.json "drops") in a data crate.
            if (!Target.killedByNpc && !IsWingman && !IsTurret)
            {
                int bp = Modding.ModBlueprints.RollDrop(Race, Spec.ship);
                if (bp >= 0 && !Crate.HoldsAny(bp)) Crate.Spawn(transform.position, new[] { new ItemStack(bp, 1) }, Race, Crate.LookJunk);
            }
            if (IsWingman) Wingmen.Died(Target.displayName);   // Level::wingmanDied: gone from the contract
            if (Spec.ship == 14 && !Target.killedByNpc) Achievements.Progress(39, ++Session.BattleshipsDestroyed);   // Status+0x118
            Current = State.Dying;
            sparks?.Clear();
            deathDir = transform.forward;
            if (engine != null) engine.Stop();
            Sfx.PlayAt(assets != null ? CombatAssets.Pick(assets.shipDestroyed) : null, transform.position);
            if (IsTurret)
            {
                // PlayerTurret::update: sound 22, an explosion with fire streaks, the turret gone at once, inactive after 4500 ms.
                dyingMs = TurretDeathMs;
                if (modelGo != null) modelGo.SetActive(false);
                rig?.HideAll();
                Explosion.Spawn(0, transform.position, transform.forward, 1f, assets != null ? CombatAssets.Pick(assets.garbageExplosion) : null, true);
                return;
            }
            if (Spec.group == NpcGroup.Outpost) traffic.PirateStationAction(false);   // a pirate base's outpost destroyed
            // PlayerFixedObject::update: every turret of the level goes too (remake, CapitalShips: an enhanced one's own only; in a
            // fleet battle the Vossk battleship's stay).
            if (Spec.ship == 14) { if (Spec.capitalEnhanced) traffic.DestroyCapitalTurrets(Spec); else traffic.DestroyTurrets(); }
            if (IsFixed || IsFreighter)
            {
                // PlayerFixedObject::update 0x17f6b4: record 22 (23 for the battleship and the Pirate Outpost) on the wreck,
                // and an explosion with fire streaks at once; the big one follows when the wreck animation ends.
                wreckBurn ??= new WreckBurn(transform, Spec.ship == 14 || Spec.fixedObject == "station_pirates");
                wreckBurn.SetEmitting(true);
                Explosion.Spawn(0, transform.position, transform.forward, 1f, null, true);
            }
            if (IsFixed)
            {
                DropCrate();
                // PlayerFixedObject::update state 3: the hull swapped for the wreck animation (plays once), smoke.
                dyingMs = Spec.deathMs > 0f ? Spec.deathMs : 20000f;
                if (Spec.wreckPrefab != null && modelGo != null)
                {
                    wreck = Instantiate(Spec.wreckPrefab, transform, false);
                    wreckDamageMs = 140f;
                    float len = PartAnimation.PlayOnce(wreck);
                    if (len > 0f) dyingMs = len;
                    modelGo.SetActive(false);
                }
                crate?.DelayExpiry(dyingMs);   // the crate's 60 s run from state 4, the end of the wreck animation
                foreach (var c in moreCrates) if (c != null) c.DelayExpiry(dyingMs);
            }
            else if (IsFreighter)
            {
                dyingMs = 10000f;
                var wreckPrefab = Spec.modCapital ? null : WreckPrefab(assets, Spec.ship, Race);   // a mod's hull burns as it is
                if (wreckPrefab != null && model != null)
                {
                    wreck = Instantiate(wreckPrefab, transform, false);
                    // PlayerFixedObject::update state 3: the wreck takes the hull's matrix as it is (AEGeometry::setMatrix),
                    // so it faces the same way and keeps the battleship's x2. Its collision boxes are another matter
                    // (CollisionVolume.ForWreck).
                    wreck.transform.localRotation = model.localRotation;
                    wreck.transform.localScale = model.localScale;
                    wreckDamageMs = 140f;
                    float len = PartAnimation.PlayOnce(wreck);
                    if (len > 0f) dyingMs = len;
                    modelGo.SetActive(false);
                }
                DropCrate();
            }
            else
            {
                dyingMs = 1500f + Random.Range(0, 1500);
                // PlayerFighter: the death burn (record 9, +0x19c) through the tumble.
                burn ??= new ShipBurn(transform);
                burn.SetBurning(true);
                spinAxis = new Vector3(Random.Range(0, 200) - 100, Random.Range(0, 200) - 100, Random.Range(0, 200) - 100).normalized;
            }
        }

        /// <summary>A freighter's wreck (setWreckedMeshId: cargo_00N_(race)_explosion_anim by race, the battleship's for
        /// ship 14; CombatAssets.wrecks), null when missing.</summary>
        public static GameObject WreckPrefab(CombatAssets assets, int ship, int race) =>
            assets != null && assets.wrecks != null && assets.wrecks.Length == 5 ? assets.wrecks[WreckIndex(ship, race)] : null;

        /// <summary>The CombatAssets.wrecks index: Terran 0, Vossk 1, Nivelian 2, Midorian 3, the battleship 4.</summary>
        public static int WreckIndex(int ship, int race) => ship == 14 ? 4 : race == 1 ? 1 : race == 2 ? 2 : race == 3 ? 3 : 0;

        /// <summary>PlayerFixedObject::setDeadButSelectable 0x180248 (the Supernova wrecks with a hidden blueprint): the
        /// hull's geometry is replaced by the wreck's (+0x120, setWreckedMeshId), SetAnimationRangeInTime puts its animation
        /// at the end and nothing advances it; HP 1 and setVulnerable(false). Still lockable and dockable (docking type 3),
        /// no explosion, no fire (WreckBurn is the dying state's). Its docking points (sets 11-14) fit the wreck's end pose.</summary>
        void ShowWreckAtEnd()
        {
            Target.invulnerable = true;
            // setHitpoints(1) keeps the maximum: the lock plate reads 0 % like the original's (it read 100 %).
            Hp.vulnerable = false;
            SetHull(1, false);
            var prefab = WreckPrefab(assets, Spec.ship, Race);
            if (prefab == null) return;   // keeps the intact hull
            var w = Instantiate(prefab, transform, false);
            if (model != null)
            {
                // the wreck takes the hull's matrix, like the dying freighter's (AEGeometry::setMatrix)
                w.transform.localRotation = model.localRotation;
                w.transform.localScale = model.localScale;
            }
            PartAnimation.HoldAllAtEnd(w);
            if (modelGo != null) Destroy(modelGo);
            modelGo = w;
            model = w.transform;
            // Multiplayer: the other players' proxies load the wreck by this marker (NetProxy.BuildVisual).
            ModelPath = WreckModelPrefix + WreckIndex(Spec.ship, Race);
        }

        void UpdateDying(float dtMs)
        {
            float frames = dtMs / OriginalFrame.Ms;
            if (IsTurret) { dyingMs -= dtMs; if (dyingMs <= 0f) SetDead(); return; }
            if (IsFixed || IsFreighter)
            {
                // PlayerFixedObject: the dying clears the moving flag (0x17f6b4), the wreck stays put; 140 ms in (+0x18c > 0x8c)
                // the wreck mesh takes its damaged material (the remake left the intact textures on it).
                if (wreckDamageMs > 0f && (wreckDamageMs -= dtMs) <= 0f) CombatAssets.ApplyWreckDamage(wreck);
            }
            else
            {
                transform.Rotate(spinAxis, 0.05f * frames * Mathf.Rad2Deg, Space.World);
                transform.position += deathDir * speed * dtMs * M;
            }
            dyingMs -= dtMs;
            if (dyingMs > 0f) return;
            // PlayerFixedObject::update: setScaling(6), 8 for the Terran battleship (0x37e7) and the pirate outpost (0x37a3).
            explosion = Explosion.Spawn(transform.position, IsFixed ? Spec.explosionScale : IsFreighter ? FreighterExplosionScale : FighterExplosionScale);
            explosionSerial = explosion != null ? explosion.Serial : 0;
            wreckBurn?.SetEmitting(false);   // state 4: enableSystemEmit(false)
            Current = State.Dead;
            smoke?.SetEmitting(false);   // the end of the tumble: Explosion::start, smoke and fire off
            if (burn != null && burn.Emitting) { burn.SetBurning(false); burn.Burst(); }   // record 11 at the explosion
            deadMs = 0f;
            if (obstacle != null && !IsFixed && Spec.modCapital) obstacle.volumes = new List<CollisionVolume>();   // a mod's: no wreck
            else if (obstacle != null && !IsFixed) obstacle.volumes = CollisionVolume.ForWreck(Spec.ship, Race);   // setWreckedMeshId
            // A Pirate Outpost's wreck (0x37a3 -> wreck volume 5): in state 4 only the wreck volumes collide.
            else if (obstacle != null && Spec.fixedObject == "station_pirates") obstacle.volumes = CollisionVolume.ForWreckId(5);
            // Remake (CapitalShips): a capital ship without a wreck leaves nothing to fly into.
            else if (obstacle != null && Spec.capitalEnhanced && wreck == null) obstacle.volumes = new List<CollisionVolume>();
            if (!IsFreighter) DropCrate();
        }

        /// <summary>KIPlayer::captureCrate on a dying or dead ship clears +0x48: the explosion leaves no crate.</summary>
        public void LootTaken() => crateDropped = true;

        /// <summary>KIPlayer::createCrate(0) for a living ship (TractorBeam::update's steal): a container of its cargo at the
        /// ship; the capture takes from the ship's own list (StealFrom).</summary>
        public Crate CreateStealCrate()
        {
            if (!HasCargo) return null;
            var prefab = assets != null ? assets.Crate(Race) : null;
            var go = prefab != null ? Instantiate(prefab, transform.position, Random.rotation) : new GameObject("Crate");
            go.name = "Stolen cargo";
            var c = go.AddComponent<Crate>();
            c.Setup(loot, Race);
            c.stolenFrom = this;
            c.pulled = true;
            stealCrate = c;
            return c;
        }

        /// <summary>KIPlayer::captureCrate on a living ship: 'amount' of 'item' leaves its hold.</summary>
        public void StealFrom(int item, int amount)
        {
            var s = loot.Find(x => x.item == item && x.amount > 0);
            if (s != null) s.amount = Mathf.Max(0, s.amount - amount);
        }

        /// <summary>Remake (CapitalShips): the cargo it drops when it dies (a capital ship's crate).</summary>
        public void SetLoot(List<ItemStack> items) { loot = items ?? new List<ItemStack>(); multiKindLoot = true; }   // a capital ship's

        void DropCrate()
        {
            // A fixed object dropped its crate as it died (OnDied) and again at the end of its dying (UpdateDying): a pirate
            // outpost's loot came twice. One crate per death.
            if (crateDropped || loot.Count == 0 || assets == null) return;
            crateDropped = true;
            // The beam was already pulling the ship's own crate (KIPlayer+0x74): that one is its crate now.
            if (stealCrate != null)
            {
                crate = stealCrate;
                crate.stolenFrom = null;
                crate.fromFriend = Target.friendToPlayer;
                crate.missionLoot = MissionShip;
                Target.crate = crate;
                return;
            }
            // Remake: loot of several kinds (a pirate outpost's by its tier, a capital ship's) drops a crate per kind (one
            // crate still holds all of one thing), thrown off in different directions; the original's ships drop one stack.
            var stacks = loot.FindAll(s => s != null && s.amount > 0);
            bool split = stacks.Count > 1 && Spec.missionCrate < 0 && multiKindLoot;
            var dirs = split ? SpreadDirections(stacks.Count) : null;
            for (int i = 0; i < (split ? stacks.Count : 1); i++)
            {
                var c = MakeCrate(split ? new List<ItemStack> { stacks[i] } : loot);
                if (split) c.Launch(dirs[i], CrateBurst);
                if (i == 0) { crate = c; Target.crate = c; }
                else moreCrates.Add(c);
            }
        }

        Crate MakeCrate(List<ItemStack> items)
        {
            var prefab = assets.Crate(Race);
            var go = prefab != null ? Instantiate(prefab, transform.position, Random.rotation) : new GameObject("Crate");
            go.name = "Crate";
            var c = go.AddComponent<Crate>();
            c.Setup(items, Race);
            c.fromFriend = Target.friendToPlayer;
            c.missionCrate = Spec.missionCrate >= 0;
            c.missionLoot = MissionShip;   // multiplayer: only the mission's team takes it
            return c;
        }

        /// <summary>'n' directions spread evenly over a sphere (a Fibonacci spiral), turned at random.</summary>
        static Vector3[] SpreadDirections(int n)
        {
            var dirs = new Vector3[n];
            var turn = Random.rotation;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = n == 1 ? 0f : 1f - 2f * (i + 0.5f) / n, r = Mathf.Sqrt(1f - y * y), a = golden * i;
                dirs[i] = turn * new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            return dirs;
        }

        void UpdateDead(float dtMs)
        {
            deadMs += dtMs;
            if ((IsFreighter || IsFixed) && wreck != null) return;   // state 4: the wreck stays for the rest of the level
            if (deadMs > 300f)
            {
                if (modelGo != null && modelGo.activeSelf) modelGo.SetActive(false);
                if (wreck != null) Destroy(wreck);
            }
            bool exploded = explosion == null || explosion.Done(explosionSerial);
            if (exploded && crate == null && moreCrates.TrueForAll(c => c == null) && deadMs > 300f) SetDead();
        }
    }
}
