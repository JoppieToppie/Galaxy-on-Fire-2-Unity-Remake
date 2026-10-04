// PlayerHealth.cs
// The player ship's Player object (Reference/research/ship_combat.md 2, 6), on the player next to ShipController:
//   Level::createPlayer 0xbca00     hull = ships.json armor, shield = attr 18 of the mounted shield (sort 9), armor = attr
//                                   20 of the mounted armor (sort 10); hit cube half-size 1200; no EMP points (immune)
//   PlayerEgo::update               shield regen (>= 101 ms ticks, + max * 100 / attr 19, no delay after hits), repair bot
//                                   (Ketar 600 / 1000 ms, Ketar II 420 / 700 ms); invulnerable during the launch / arrival
//                                   camera and the jump scenes (LevelScript, startJumpScene)
//                                   hit feedback when shield + armor + hull dropped: camera shake 1000 ms (+-6 units),
//                                   sound 25 / 23 / 24 by the layer hit, the shield icon turns red for 500 ms (while
//                                   shield >= 2), a directional arc (left / right / top / bottom) for 300 ms
//   PlayerEgo::calcCollision 0xab550  collisions: PlayerCollision (asteroids cost 20, stations / ships none)
//   MGame::gameOverCheck / PlayerEgo::explode  hull < 1: the camera freezes, the ship tumbles, explodes at 3 s, "Game Over"
//                                   and sound 37 at 8 s; then FlightHud offers "Tap to load last savegame." (196)
// Hull / shield / armor are kept in Session between levels (-1 = full; docking repairs, see StationLevel).
// Emergency system (item 185, combat_equipment.md 2): the hull running out sets it to 1 instead, 10 s invulnerable (attr 41)
//   inside the v_shield bubble (grows / shrinks over 5 %), sound 1115, the item is used up; kills meanwhile count for
//   medal 43. Shield injector (227, 3.5): an empty shield takes 30 t Blue Plasma (item 202) and refills at 0.15 per ms
//   (at least 1 per frame), sounds 2258 / 2257 / 2259. Gamma (3.6): in the supernova orbits (109-113) a 0..100 pool drains
//   at the station's rate (less with a gamma shield: x (100 - attr 52) / 100), "Warning: Gamma shield low" (3201) below 15,
//   death at 0; full again in any other orbit.
// Not yet: volatile cargo. EMP immunity is implicit (no points).

using System;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class PlayerHealth : MonoBehaviour
    {
        const float M = 0.05f;
        public const float HitRadiusUnits = 1200f;

        public Target Target { get; private set; }
        public Hitpoints Hp => Target.hitpoints;
        public bool HasShield { get; private set; }
        public bool HasArmor { get; private set; }
        /// <summary>ms left of the red shield icon (Hud::playerHit).</summary>
        public float ShieldHitMs { get; private set; }
        /// <summary>ms left per hit arc: 0 left, 1 right, 2 top, 3 bottom (Hud::draw, 300 ms).</summary>
        public readonly float[] ArcMs = new float[4];
        public bool Dead { get; private set; }
        /// <summary>The gamma shield's loop plays in the engine's place (PlayerEngine stays silent).</summary>
        public bool GammaLoopActive { get; private set; }
        public bool GameOver { get; private set; }
        /// <summary>Set by the level each frame: the launch camera / jump scenes (Player::setVulnerable(false)).</summary>
        [NonSerialized] public bool invulnerable;
        public event Action GameOverStarted;
        /// <summary>A HUD message ("-30t Blue Plasma", "Warning: Gamma shield low").</summary>
        public event Action<string> Message;
        /// <summary>The emergency system's bubble is up (PlayerEgo::emergencySystemActive).</summary>
        public static bool EmergencyActive { get; private set; }
        /// <summary>The supernova radiation pool (0..100), -1 = none in this orbit.</summary>
        public float Gamma { get; private set; } = -1f;

        ShipController ship;
        ChaseCamera chase;
        WeaponSystem weapons;
        CombatAssets assets;
        AudioSource sfx;
        int shieldRechargeMs;
        float repairHullMs, repairArmorMs;
        bool hasRepair, exploded;
        float lastCombined, deathMs;
        // emergency system
        bool hasEmergency;
        float emergencyMs, emergencyLength = 10000f, bubbleScale;
        GameObject bubble;
        Material bubbleMaterial;
        // shield injector
        bool hasInjector, injecting;
        int injectorCost = 30;
        AudioSource injectorLoop;
        // gamma
        float gammaRate;
        bool gammaWarned;

        public void Setup(Database db, ShipController controller, ChaseCamera chaseCamera, WeaponSystem weaponSystem)
        {
            ship = controller;
            chase = chaseCamera;
            weapons = weaponSystem;
            assets = CombatAssets.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;

            int hull = (db.Ship(Session.ShipIndex)?.armor ?? 100) + (Session.HasMod(0) ? 40 : 0);   // Ship::getMaxHP: +40 with mod 0
            var shieldItem = Shop.FirstMounted(db, 9);
            var armorItem = Shop.FirstMounted(db, 10);
            var repair = Shop.FirstMounted(db, 15);
            int shield = shieldItem != null ? shieldItem.Attr(18) : 0;
            shieldRechargeMs = shieldItem != null ? shieldItem.Attr(19) : 0;
            int armor = armorItem != null ? armorItem.Attr(20) : 0;
            HasShield = shield > 0;
            HasArmor = armor > 0;
            if (repair != null) { hasRepair = true; bool mk2 = repair.index != 75; repairHullMs = mk2 ? 420f : 600f; repairArmorMs = mk2 ? 700f : 1000f; }

            Target = gameObject.AddComponent<Target>();
            Target.isPlayer = true;
            Target.isShip = true;
            Target.race = 0;   // the player's ship counts as Terran in NPC race checks
            Target.customDeath = true;
            Target.radius = HitRadiusUnits * M;
            Target.hitpoints = new Hitpoints(hull, shield, armor);
            if (Session.PlayerHull >= 0) Hp.hull = Mathf.Clamp(Session.PlayerHull, 1, hull);
            if (Session.PlayerArmor >= 0) Hp.armor = Mathf.Clamp(Session.PlayerArmor, 0, armor);
            if (Session.PlayerShield >= 0f) Hp.shield = Mathf.Clamp(Session.PlayerShield, 0f, shield);
            Target.hp = Hp.hull;
            Target.maxHp = hull;
            lastCombined = Hp.Combined;
            if (weapons != null) weapons.Owner = Target;

            var emergency = Shop.FirstMounted(db, 27);
            if (emergency != null) { hasEmergency = true; emergencyLength = emergency.Attr(41, 10000); }
            Target.SaveFromDeath = TryEmergency;
            EmergencyActive = false;
            var injector = Shop.FirstMounted(db, 43);
            if (injector != null) { hasInjector = true; injectorCost = injector.Attr(59, 30); }
            SetupGamma(db);
            SetupBlaze(db);
        }

        // ---- emergency system (PlayerEgo::tryToStartEmergencySystem 0xad6f0) ------------------------------------

        bool TryEmergency()
        {
            if (!hasEmergency || emergencyMs > 0f || Dead) return false;
            hasEmergency = false;
            Hp.hull = 1;
            Target.hp = 1;
            emergencyMs = emergencyLength;
            EmergencyActive = true;
            Session.Equipment.RemoveAll(e => e.item == 185);   // used up for good
            // 1115 Invincibile is a loop: PlayerEgo::update stops it when the shield ends.
            if (assets != null && assets.invincibility != null)
            {
                if (invincibleLoop == null) { invincibleLoop = gameObject.AddComponent<AudioSource>(); invincibleLoop.loop = true; invincibleLoop.spatialBlend = 0f; invincibleLoop.playOnAwake = false; }
                invincibleLoop.clip = assets.invincibility;
                invincibleLoop.volume = Settings.SfxVolume;
                invincibleLoop.Play();
            }
            if (assets != null && assets.shieldBubble != null)
            {
                bubble = Instantiate(assets.shieldBubble, ship.visualModel != null ? ship.visualModel : transform, false);
                GunRig.StripForFx(bubble);
                // Material 27150 draws with SimpleRefractionShader: an invisible sphere bending the screen behind its rim.
                if (assets.shieldBubbleShader != null)
                    foreach (var br in bubble.GetComponentsInChildren<Renderer>())
                    {
                        var src = br.sharedMaterial;
                        bubbleMaterial = new Material(assets.shieldBubbleShader);
                        if (src != null && src.HasProperty("_BaseMap")) bubbleMaterial.SetTexture("_NoiseMap", src.GetTexture("_BaseMap"));
                        br.sharedMaterial = bubbleMaterial;
                    }
                OpaqueTexture.Request(this, true);
                var r = ship.visualModel != null ? ship.visualModel.GetComponentInChildren<Renderer>() : null;
                float radiusUnits = r != null ? r.bounds.extents.magnitude / M : 1500f;
                bubbleScale = radiusUnits / 500f + 0.1f;
                bubble.transform.localScale = Vector3.zero;
            }
            return true;
        }

        void UpdateEmergency(float dtMs)
        {
            if (emergencyMs <= 0f) return;
            emergencyMs -= dtMs;
            Hp.vulnerable = false;
            if (bubble != null)
            {
                float t = emergencyMs, d = emergencyLength, edge = 0.05f * d;
                float f = t > d - edge ? (d - t) / edge : t < edge ? t / edge : 1f;
                bubble.transform.localScale = Vector3.one * bubbleScale * Mathf.Clamp01(f);
            }
            if (bubbleMaterial != null) bubbleMaterial.SetFloat("_Anim", (emergencyLength - emergencyMs) * 0.001f);   // mesh+0x24 += dt * 0.001
            if (emergencyMs <= 0f) EndEmergency();
        }

        AudioSource invincibleLoop;

        void EndEmergency()
        {
            EmergencyActive = false;
            if (invincibleLoop != null) invincibleLoop.Stop();
            if (bubble != null) Destroy(bubble);
            if (bubbleMaterial != null) Destroy(bubbleMaterial);
            OpaqueTexture.Request(this, false);
        }

        // ---- shield injector (PlayerEgo::update 0xa8f4e) ------------------------------------------------------

        void UpdateInjector(float dtMs)
        {
            if (!hasInjector || Hp.maxShield <= 0) return;
            if (!injecting)
            {
                if (Hp.shield >= 1f || Shop.CargoOf(202) < injectorCost) return;
                Shop.RemoveFromCargo(202, injectorCost);
                injecting = true;
                Message?.Invoke($"-{injectorCost}t {Localization.Get(1476)}");
                if (assets != null && assets.injectorInit != null) sfx.PlayOneShot(assets.injectorInit, Settings.SfxVolume);
                if (assets != null && assets.injectorLoop != null)
                {
                    if (injectorLoop == null) { injectorLoop = gameObject.AddComponent<AudioSource>(); injectorLoop.loop = true; injectorLoop.spatialBlend = 0f; }
                    injectorLoop.clip = assets.injectorLoop;
                    injectorLoop.volume = Settings.SfxVolume;
                    injectorLoop.Play();
                }
            }
            if (Hp.shield < Hp.maxShield)
            {
                Hp.shield = Mathf.Min(Hp.maxShield, (int)(Hp.shield + Mathf.Max(dtMs * 0.15f, 1f)));
                if (injectorLoop != null) injectorLoop.pitch = 0.8f + 0.4f * Hp.ShieldFraction;   // FMOD parameter 0 = the shield fraction
                return;
            }
            injecting = false;
            if (injectorLoop != null) injectorLoop.Stop();
            if (assets != null && assets.injectorEnd != null) sfx.PlayOneShot(assets.injectorEnd, Settings.SfxVolume);
        }

        // ---- gamma (Status::getGammaRayDamagePerSecond 0xba160, Level::update) ------------------------------------

        void SetupGamma(Database db)
        {
            int st = Session.StationIndex, cm = Session.CampaignMission;
            float[] early = { 0.7f, 0.4f, 0.4f, 0.3f, 0.2f }, mid = { 3f, 2f, 1f, 0.5f, 0.3f };
            float rate = st < 109 || st > 113 ? 0f : cm < 106 ? early[st - 109] : cm < 158 ? mid[st - 109] : st == 109 ? 1f : 0f;
            var gammaShield = Shop.FirstMounted(db, 38);
            if (rate > 0f && gammaShield != null) rate *= (100 - gammaShield.Attr(52)) / 100f;
            gammaRate = rate;
            if (rate <= 0f) { Gamma = -1f; Session.PlayerGamma = -1f; return; }
            Gamma = Session.PlayerGamma >= 0f ? Session.PlayerGamma : 100f;
            gammaWarned = Gamma < 15f;
            var clip = gammaShield == null || assets == null ? null : gammaShield.index == 205 ? assets.gammaShield1 : assets.gammaShield2;   // PlayerEgo::PlayerEgo: 0xcd 2261, else 2260
            if (clip != null)
            {
                // The gamma shield's loop takes the engine sound's slot (PlayerEgo+0x1c): event volume 0.09.
                var loop = gameObject.AddComponent<AudioSource>();
                loop.clip = clip; loop.loop = true; loop.spatialBlend = 0f; loop.volume = 0.09f * Sfx.EventGain * Settings.SfxVolume; loop.Play();
                GammaLoopActive = true;
            }
        }

        // ---- the gamma shield's blaze (PlayerEgo::PlayerEgo 0xa5d8c, PlayerEgo::update 0xa9b7c, PlayerEgo::render) --------
        // In the supernova system or Luur's orbit (Status::inSupernovaSystem / inSupernovaOrbit) with a gamma shield (sort
        // 0x26) mounted: meshes 18803 sn_ship_blaze_flames_anim_add + 18802 sn_ship_blaze_glow_anim_add in one group (+0x34,
        // shown +0x38), every frame scaled by +0x3c (the ship model's bounding radius x 1.75, PlayerEgo::setShip), turned to
        // the sun's light direction with up (0, 1, 0), at the ship (ship 8 Kinzer: 300 units ahead), its animations running;
        // hidden for good by the death (PlayerEgo::update, hudEvent 0x1a) and the planet jump (dockToPlanet).

        GameObject blaze;
        Vector3 blazeSunDir;
        GoF2Remake.World.SpaceLevel blazeLevel;

        void SetupBlaze(Database db)
        {
            if (assets == null || (assets.gammaBlazeFlames == null && assets.gammaBlazeGlow == null) || Shop.FirstMounted(db, 38) == null) return;
            int st = Session.StationIndex;
            if (!Shop.InSupernovaSystem(Shop.SystemOf(db, st), st) && st != 109) return;
            blazeLevel = FindAnyObjectByType<GoF2Remake.World.SpaceLevel>();
            if (blazeLevel == null || blazeLevel.Layout == null) return;
            blazeSunDir = GoF2Remake.World.OrbitLayout.DirToUnity(blazeLevel.Layout.lightDirection).normalized;
            blaze = new GameObject("GammaShieldBlaze");
            blaze.transform.SetParent(transform, false);
            foreach (var prefab in new[] { assets.gammaBlazeFlames, assets.gammaBlazeGlow })
            {
                if (prefab == null) continue;
                var part = Instantiate(prefab, blaze.transform, false);
                GunRig.StripForFx(part);
            }
            var r = ship.visualModel != null ? ship.visualModel.GetComponentInChildren<Renderer>() : null;
            float radiusUnits = r != null ? r.bounds.extents.magnitude / M : 1500f;   // the bubble's radius too
            blaze.transform.localScale = Vector3.one * radiusUnits * 1.75f;
        }

        void LateUpdate()
        {
            if (blaze == null) return;
            if (Dead || (blazeLevel != null && blazeLevel.Navigation != null && blazeLevel.Navigation.Jumping)) { Destroy(blaze); blaze = null; return; }
            var model = ship.visualModel != null ? ship.visualModel : transform;
            var pos = model.position;
            if (Session.ShipIndex == 8) pos += model.forward * (300f * M);
            blaze.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(blazeSunDir, Vector3.up));
        }

        /// <summary>The Debug panel's repair: the gamma pool back to 100.</summary>
        public void RefillGamma()
        {
            if (Gamma < 0f) return;
            Gamma = 100f;
            Session.PlayerGamma = Gamma;
            gammaWarned = false;
        }

        void UpdateGamma(float dtMs)
        {
            if (Gamma < 0f || !Hp.vulnerable || Cheats.GodMode) return;
            Gamma = Mathf.Max(0f, Gamma - dtMs * gammaRate / 1000f);
            Session.PlayerGamma = Gamma;
            if (!gammaWarned && Gamma < 15f) { gammaWarned = true; Message?.Invoke(Localization.Get(3201)); }
            if (Gamma < 1f) { Hp.hull = 0; Target.hp = 0; }   // PlayerEgo::update: the gamma pool empty = death
        }

        void OnDestroy()
        {
            OpaqueTexture.Request(this, false);
            EmergencyActive = false;
            // Docking / jumping saves the ship state to Status (MGame::dockEvent, departStation).
            if (Target != null && Hp != null && !Dead)
            {
                Session.PlayerHull = Hp.hull;
                Session.PlayerArmor = Hp.armor;
                Session.PlayerShield = Hp.shield;
            }
        }

        void Update()
        {
            if (Target == null) return;
            float dtMs = Time.deltaTime * 1000f;
            ShieldHitMs = Mathf.Max(0f, ShieldHitMs - dtMs);
            for (int i = 0; i < 4; i++) ArcMs[i] = Mathf.Max(0f, ArcMs[i] - dtMs);
            if (Dead) { UpdateDeath(dtMs); return; }

            Hp.vulnerable = !invulnerable;
            UpdateEmergency(dtMs);
            UpdateGamma(dtMs);
            Hp.RegenerateShield(dtMs, shieldRechargeMs);
            UpdateInjector(dtMs);
            if (hasRepair) Hp.Repair(dtMs, repairHullMs, repairArmorMs);
            Target.hp = Hp.hull;

            float combined = Hp.Combined;
            if (combined < lastCombined) OnHit();
            lastCombined = combined;

            if (!Hp.Alive) StartDeath();
        }

        /// <summary>PlayerEgo::update hit feedback.</summary>
        void OnHit()
        {
            GetComponent<VolatileCargo>()?.Add(0.065f);   // Player::damage: the volatile meter + 0.065 per hit
            if (chase != null && chase.enabled) chase.Shake(1000f, 6f);
            if (Hp.shield >= 2f) ShieldHitMs = 500f;
            AudioClip clip = null;
            if (Hp.hullHit) clip = CombatAssets.Pick(assets?.hitHull);
            else if (Hp.armorHit) clip = CombatAssets.Pick(assets?.hitArmor);
            else if (Hp.shieldHit) clip = CombatAssets.Pick(assets?.hitShield);
            // Remake: haptics by the deepest layer hit, like the sound.
            Haptics.Play(Hp.hullHit ? Haptics.HitHull : Hp.armorHit ? Haptics.HitArmor : Haptics.HitShield);
            Hp.shieldHit = Hp.armorHit = Hp.hullHit = false;
            if (clip != null) sfx.PlayOneShot(clip, Settings.SfxVolume);

            // Hit direction: where the shot came from, seen from the camera (left / right off screen, else top / bottom).
            var cam = Camera.main;
            var v = Target.lastHitVector;
            if (cam == null || v.sqrMagnitude < 1e-10f) return;
            var from = cam.transform.InverseTransformDirection(-v.normalized);
            float halfW = Mathf.Tan(Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect) * 0.5f * Mathf.Deg2Rad);
            if (from.z <= 0f || from.x / from.z < -halfW) { if (from.x < 0f) ArcMs[0] = 300f; }
            if (from.z <= 0f || from.x / from.z > halfW) { if (from.x > 0f) ArcMs[1] = 300f; }
            if (from.z > 0f) ArcMs[3] = 300f; else ArcMs[2] = 300f;
        }

        // ---- death (PlayerEgo::explode 0xada6c, MGame::gameOverCheck 0x1b0d04) ---------------------------------

        /// <summary>Player::setHitpoints(0) (MGame::OnUpdate: entering the wormhole too early): death at once, no emergency system.
        /// 'force' (multiplayer's /kill) also passes god mode.</summary>
        public void Kill(bool force = false)
        {
            if (Dead || (Cheats.GodMode && !force)) return;
            hasEmergency = false;
            Hp.hull = 0;
            Target.hp = 0;
            StartDeath();
        }

        void StartDeath()
        {
            if (TryEmergency()) return;
            Dead = true;
            if (EmergencyActive) EndEmergency();
            if (injectorLoop != null) injectorLoop.Stop();
            deathMs = 0f;
            ship.ExternalSpeedMetersPerSecond = ship.SpeedMetersPerSecond;
            ship.externalControl = true;
            ship.modelTumbling = true;   // the tumble below; the controller's levelling had reset it every frame
            ship.autopilotTarget = null;
            if (weapons != null) weapons.Blocked = true;
            if (chase != null) chase.enabled = false;   // TargetFollowCamera::setActive(false): the camera stays where it is
            Haptics.Play(Haptics.Crippled);   // remake
            // PlayerEgo::explode 0xada6c: the death burn (record 9) from the first frame until the explosion.
            burn ??= new ShipBurn(transform);
            burn.SetBurning(true);
        }

        ShipBurn burn;

        void UpdateDeath(float dtMs)
        {
            deathMs += dtMs;
            if (!exploded)
            {
                transform.position += transform.forward * ship.ExternalSpeedMetersPerSecond * dtMs / 1000f;
                // PlayerEgo::update 0xab2aa: the model's Euler angles + 0.03 rad per (30 fps) frame on every axis.
                float step = 0.03f * Mathf.Rad2Deg * (dtMs / (1000f / 30f));
                if (ship.visualModel != null) ship.visualModel.localRotation *= Quaternion.Euler(step, step, step);
            }
            if (!exploded && deathMs >= 3000f)
            {
                exploded = true;
                Explosion.Spawn(transform.position);
                Haptics.Play(Haptics.Death);   // remake (the frozen camera takes no explosion rumble)
                // At 3000 ms: the burn stops and record 11 bursts once at the ship.
                if (burn != null) { burn.SetBurning(false); burn.Burst(); }
                if (ship.visualModel != null) ship.visualModel.gameObject.SetActive(false);
                ship.ExternalSpeedMetersPerSecond = 0f;
            }
            // Remake multiplayer: an event's respawn point brings the ship back in space after its delay (NetEventRespawn).
            if (exploded && Multiplayer.NetEventRespawn.Due(deathMs)) { Multiplayer.NetEventRespawn.Go(); return; }
            if (!GameOver && deathMs >= 8000f)
            {
                GameOver = true;
                if (assets != null && assets.gameOver != null) sfx.PlayOneShot(assets.gameOver, Settings.SfxVolume);
                GameOverStarted?.Invoke();
            }
        }
    }
}
