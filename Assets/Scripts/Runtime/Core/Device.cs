// Device.cs
// Remake: an active device, an equipment item fired from the quick menu or its key, with its own cooldown and ready state,
// as plain rules (Modding/README.md "Devices"). The devices are Galaxy on Fire 3's (its DeviceConfiguration records, the
// gof3_ships importer's items): a mod item with the remake attribute device (150, the type below) on a seatless cabin
// (base 91, cabinSize 0), its own one-per-ship kind per type (Shop.KindOf: KindBase + type). PlayerDevices runs them.
//   Ready     fired (TryFire) -> Active for deviceDurationMs (0 = an instant device: straight to the cooldown)
//   Active    the effect runs (a slow, a damage boost, the deflector); its end starts the cooldown
//   Cooldown  deviceCooldownMs (GoF3: 60 s each), then Ready ("{0} ready")
// The strike charge also needs its charge full (deviceCharge > 0): the damage the ship's weapons deal fills it while it
// isn't running (AddCharge), and firing empties it.
// GoF3's distances are in its own units: x 11.4 for GoF2's (its EMP generator's 800 = 9120 units, 456 m).

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class Device
    {
        public enum Type
        {
            None = 0,
            /// <summary>Berger Area EMP: a wave from the ship out to deviceRadius over deviceGrowMs; each hostile ship it
            /// reaches is slowed to deviceSlowPercent of its speed and can't turn for deviceDurationMs.</summary>
            AreaEmp = 1,
            /// <summary>Berger EMP Projectile: a homing shot at the lock (speed 13, lifetimeMs 12); its hit slows every hostile
            /// within deviceRadius like the area EMP.</summary>
            EmpShot = 2,
            /// <summary>DS Shield Disruptor: a homing shot whose hit takes 'damage' (9) off the target's shield; a ship without
            /// one (the NPCs) takes deviceDamagePercent of it as damage.</summary>
            ShieldDisruptor = 3,
            /// <summary>Telta S-Wave Area: spends deviceShieldCost % of the ship's maximum shield; every hostile within
            /// deviceRadius takes deviceDamagePercent of what was spent.</summary>
            OverchargeArea = 4,
            /// <summary>Telta Overcharger Beam: the same cost; every hostile within deviceRadius inside deviceConeDeg of the nose
            /// takes deviceDamagePercent of it.</summary>
            OverchargeBeam = 5,
            /// <summary>AMR Strike Charge: filled by deviceCharge points of damage dealt, then deviceDurationMs of the primary
            /// weapons' damage x deviceDamagePercent / 100.</summary>
            StrikeCharge = 6,
            /// <summary>Carom Deflector: for deviceDurationMs the enemy shots that hit the ship are turned back at their shooter
            /// at deviceDamagePercent of their damage.</summary>
            Deflector = 7,
            // Remake (2026-11-02): EVERSPACE 2's consumables (its BP_Consumable_* blueprints), usually with deviceConsumable
            // (one use per unit, stacked like missiles; PlayerDevices).
            /// <summary>Smoke Bomb: a sensor cloud of deviceRadius around the ship for deviceDurationMs (grown over deviceGrowMs,
            /// ES2's 2 s): while the ship is inside, the enemies lose it (as under a cloak: no fire, no missile locks).</summary>
            SensorCloud = 8,
            /// <summary>Nano Injector: deviceDamagePercent % of the hull's maximum repaired at once (only with hull missing); with a
            /// deviceDurationMs, over that time (ES2's Nanobots: their consumable_effect over consumable_duration).</summary>
            HullRepair = 9,
            /// <summary>Armor Repair: deviceDamagePercent % of the armour's maximum repaired at once (ES2: to full; only with
            /// armour missing).</summary>
            ArmorRepair = 10,
            /// <summary>Secondaries Restock: deviceAmmo rounds for the selected secondary (ES2: a share of its maximum ammo).</summary>
            Restock = 11,
            /// <summary>Device Charger: every other device's cooldown ends at once (ES2's ResetCooldowns; only while one cools down).</summary>
            DeviceCharger = 12,
            // Remake (2026-11-02): EVERSPACE 2's cooldown devices with mechanics of their own (its BP_Device_* blueprints).
            /// <summary>Corrosion Injector: the locked ship (within deviceRadius) takes 'damage' every 0.95 s for deviceDurationMs,
            /// past its shield (ES2's Corrosion condition: shield-piercing kinetic damage over time).</summary>
            Corrosion = 13,
            /// <summary>Teleporter: a blink deviceRadius ahead, stopped short of a hull, a station or an asteroid in the way (ES2's
            /// sphere trace and 10 m SafetyMargin); refused with no room at all.</summary>
            Teleport = 14,
            /// <summary>Spectral Shift: the same blink, and every hostile ship within deviceRange2 of the path takes 'damage'.</summary>
            SpectralShift = 15,
            /// <summary>Fusion Hook: the hostile ship in the sights (within deviceRadius, 18 deg of the nose) is grappled for up to
            /// deviceDurationMs: after 0.3 s the ship is pulled toward it (up to 800 m/s) and it toward the ship, until they are
            /// within 200 m, it leaves the hook's 37 deg, or it dies.</summary>
            FusionHook = 16,
            /// <summary>Quantum Tether: for deviceDurationMs the damage the locked ship takes is dealt to every hostile ship within
            /// deviceRadius of it too; when it dies the tether jumps to one of them.</summary>
            QuantumTether = 17,
            /// <summary>Quantum Entangler: for deviceDurationMs the damage the ship takes is summed and, at the end, dealt to the
            /// locked ship x deviceDamagePercent / 100 (nothing if it died first).</summary>
            Entangler = 18,
            // Remake (2026-11-04): more of EVERSPACE 2's consumables and devices.
            /// <summary>Nano Repair Kit: one of the ship's overheated items (Flight.Overheat) works again at once (ES2: "repairs a
            /// random damaged equipment item"; only with one overheated).</summary>
            RepairKit = 19,
            /// <summary>System Recovery Routine: the debuffs on the ship lifted (Flight.PlayerDebuffs; only with one on).</summary>
            Recovery = 20,
            /// <summary>Annihilator Virus: the locked ship (within deviceRadius) is infected; every deviceGrowMs the virus spreads to
            /// the nearest hostile ship within deviceRadius of an infected one, up to VirusMaxTargets; after deviceDurationMs every
            /// infected ship takes deviceDamagePercent % of a pirate raider's hull (ES2: 95 + 60 against its raider's 310).</summary>
            Virus = 21,
            // Remake (2026-11-11): Chorus's player capital ship (BP_Player_Auxiliary_ShieldOvercharge_PlayerCapitalShip,
            // AAuxiliaryShieldOvercharge: Duration 5, Cooldown 10).
            /// <summary>Shield Overcharge: the shield full at once, and for deviceDurationMs every hit stops at it (nothing
            /// reaches the armour or hull, the shield doesn't drop; Hitpoints.shieldLocked).</summary>
            ShieldOvercharge = 22,
            // Remake (2026-11-13): Chorus's Rites (its BP_ActivePower_* blueprints), paid from the ship's Void energy
            // (Flight.VoidEnergy) by deviceVoidCost pips besides their own short cooldown.
            /// <summary>Rite of the Hunt (Chorus's Blink, BP_ActivePower_Blink): a leap onto the tail of the hostile ship in the
            /// sights (the lock first) within deviceRadius (default HuntRangeTops of the top speed), deviceRange2 behind it
            /// (default HuntBehindTops), facing its way at the ship's own speed; not at capital ships or the pursuer.</summary>
            RiteHunt = 23,
            /// <summary>Rite of the Storm (Chorus's Pulse Zap, BP_ActivePower_Pulse_Zap + BP_Buff_Pulse): the hostile ship in the
            /// sights (the lock first) within deviceRadius takes 'empDamage' on its shield and is jammed for deviceDurationMs
            /// (default 4 s): no fire, slowed to deviceSlowPercent (default 0) and unable to turn; every deviceGrowMs (default
            /// 0.25 s) it jumps to the nearest other such ship within deviceRange2 of the last, deviceChain times (default 0;
            /// Chorus's upgrade A: 2 within 500 m). The device itself is instant.</summary>
            RiteStorm = 24,
            /// <summary>Rite of the Star (Chorus's Drill, BP_ActivePower_Drill on ADrillActivePower): for deviceDurationMs
            /// (default 3 s; a second press after 0.5 s ends it) the ship becomes a star: unseen and unharmed, it flies straight
            /// on at StarSpeedTops of its top speed turning at most StarTurnDegS, and every deviceGrowMs (default 0.2 s, and
            /// 0.3 s past its end) every hostile ship within deviceRange2 (default StarRadiusTops) of a point StarAheadTops
            /// ahead takes 'damage'.</summary>
            RiteStar = 25,
            /// <summary>Rite of Control (Chorus's Telekinesis, BP_ActivePower_Telekinesis + BP_Telekinesis_Debuff): the hostile
            /// ship in the sights (the lock first) within deviceRadius (default ControlGrabTops) is held beside the nose, jammed,
            /// for up to deviceDurationMs (default 8 s, then let go); a second press throws it at the ship in the sights within
            /// ControlTossTops (else straight ahead) at ControlSpeedTops of the top speed: whatever it hits first, a ship takes
            /// 'damage' and it dies, a station, hull or asteroid kills it.</summary>
            RiteControl = 26,
            /// <summary>Rite of the Senses (Chorus's Scan, BP_ActivePower_Scan's short range): a wave from the ship out to
            /// deviceRadius (default SensesTops) over SensesWaveMs uncovers every cloaked hostile ship it reaches, and for
            /// deviceDurationMs (default 20 s) the ship sees as the best scanners do (RiteSenses: class-A asteroids marked, a
            /// locked ship's cargo read out).</summary>
            RiteSenses = 27,
            /// <summary>A passive Rite (Chorus's PassivePowers, BP_PassivePower_ElementalShield): never fired, it works while
            /// mounted through its stats: deviceVoidPips more Void energy pips (the Elemental Shield's BonusEnergy 2),
            /// deviceElementalProof 1 = no harm from heat, frost or corrosion ("Prevents all elemental damage").</summary>
            RitePassive = 28,
        }

        public const int TypeCount = 28;
        /// <summary>The Annihilator Virus's most ships (its description's 6).</summary>
        public const int VirusMaxTargets = 6;
        /// <summary>The remake attributes (Modding.ItemStats).</summary>
        public const int TypeAttr = 150, CooldownAttr = 151, DurationAttr = 152, RadiusAttr = 153, GrowAttr = 154,
                         SlowAttr = 155, ShieldCostAttr = 156, DamagePercentAttr = 157, ChargeAttr = 158, ConeAttr = 159;
        /// <summary>Remake (2026-11-02): deviceConsumable 1 = one use per unit, the mounted stack's amount the uses left (the
        /// hangar stacks them like missiles; 0 cooldown unless given one); deviceAmmo = the restock's rounds.</summary>
        public const int ConsumableAttr = 164, AmmoAttr = 165;
        /// <summary>Remake (2026-11-02): deviceRange2 = the spectral shift's sweep radius around its path (game units).</summary>
        public const int Range2Attr = 166;
        /// <summary>Remake (2026-11-13): deviceVoidCost = the Void energy pips a Rite pays (Flight.VoidEnergy; Chorus's EnergyCost).</summary>
        public const int VoidCostAttr = 178;
        /// <summary>Remake (2026-11-13): deviceChain = the Rite of the Storm's jumps to more ships (Chorus's ChainLimit).</summary>
        public const int ChainAttr = 179;
        /// <summary>Remake (2026-11-13): deviceUpgrade = a Rite's upgrades (Chorus's A / B, bits 1 / 2); deviceVoidPips = more Void
        /// energy pips (a passive's); deviceElementalProof 1 = no harm from elemental hazards (a passive's).</summary>
        public const int UpgradeAttr = 180, VoidPipsAttr = 181, ElementalAttr = 182;
        public const int UpgradeA = 1, UpgradeB = 2;
        // The upgrades (their blueprints' graphs, read with the exporter's CHORUS_SCRIPT bytecode):
        //   Hunt A  BP_ActivePower_Blink_UpgradeA: EnergyComponent.AddBonusEnergy(1) after the leap (a bonus pip lives
        //           UEnergyComponent's BonusEnergyLifetime, 10 s: its constructor 0x140be1d60)
        //   Hunt B  BP_Player_Buff_Blink_UpgradeB: DamageMultiplier 1.5 for Duration 5 s, every weapon and Rite
        //   Storm A BP_ActivePower_Pulse_UpgradeA: ChainLimit 2, ChainRange 50000 cm
        //   Storm B BP_ActivePower_Pulse_UpgradeB: ShieldComponent.IncreaseShield(ShieldDamage x ShieldStealMultiplier 0.25)
        //   Star A  BP_ActivePower_Drill_UpgradeA: four more spheres of 10000 cm at (10000, +-6000, +-6000) cm, turning
        //           RotationSpeed 100 deg/s about the nose (bCanRotate)
        //   Star B  BP_ActivePower_Drill_UpgradeB: HealthComponent.AddHealth(max x HealPercentage 25 / 100) a ship destroyed
        public const int HuntBonusPips = 1, FuryMs = 5000, StormChainA = 2;
        public const float FuryFactor = 1.5f, StormChainTopsA = 50000f / 14000f, StealShare = 0.25f, StarHealShare = 0.25f;
        public const float StarSideAheadTops = 10000f / 14000f, StarSideOffTops = 6000f / 14000f, StarSideDegS = 100f;

        /// <summary>The Star's upgrade-A sphere 'i' (0..3) at 'ms' into its run: its centre in the ship's frame (seconds of the
        /// top speed: x right, y up, z ahead), the four turning about the nose.</summary>
        public static Vector3 StarSide(int i, float ms)
        {
            float a = (45f + 90f * i + StarSideDegS * ms / 1000f) * Mathf.Deg2Rad;
            float r = StarSideOffTops * Mathf.Sqrt(2f);   // (the corners of +-6000, +-6000)
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, StarSideAheadTops);
        }
        // Chorus's Blink (BP_ActivePower_Blink's defaults: Range 100000 cm, BlinkLocationOffset -18000 cm, EnergyCost 2,
        // Cooldown 0.5 s), its distances kept as seconds of its 140 m/s top speed (as Chorus flight's), so they scale with
        // the ship's: 1000 m = 7.14 s, 180 m = 1.29 s. TargetableRadius 0.1 (UpdateActivePowerTargets' screen share, not
        // decompiled) read as a 10 degree cone round the nose.
        public const float HuntRangeTops = 100000f / 14000f, HuntBehindTops = 18000f / 14000f, HuntAimDot = 0.9848f;
        public const int HuntCost = 2, HuntCooldownMs = 500;
        // Chorus's Pulse Zap (BP_ActivePower_Pulse_Zap: Range 100000 cm, ChainRange 10000 cm, ChainInterval 0.25 s, EnergyCost 2,
        // Cooldown 0.5, Duration 0; BP_Buff_Pulse: Duration 4 s), its distances as seconds of the top speed like the Hunt's.
        public const float StormChainTops = 10000f / 14000f;
        public const int StormCost = 2, StormCooldownMs = 500, StormJamMs = 4000, StormChainMs = 250;
        // Chorus's Drill (ADrillActivePower's constructor 0x140a4fd40: DrillForwardVelocity 42000 cm/s, MaxAngularVelocity 72
        // deg/s, MinDurationTime 0.5, MaxDurationTime 3, CooldownTime 0.5; BP_ActivePower_Drill: EnergyCost 3,
        // CheckForCollisionsInterval 0.2, PostDeactivationDrillCollisionDuration 0.3, CollisionDamage 750, its sphere trace
        // 10000 cm round a point 2500 cm ahead), the distances as seconds of the top speed.
        public const float StarSpeedTops = 42000f / 14000f, StarTurnDegS = 72f, StarAheadTops = 2500f / 14000f, StarRadiusTops = 10000f / 14000f;
        public const int StarCost = 3, StarCooldownMs = 500, StarMinMs = 500, StarMaxMs = 3000, StarTickMs = 200, StarTailMs = 300;

        // Chorus's Telekinesis (BP_ActivePower_Telekinesis: GrabRange 70000 cm, TossRange 80000, TossSpeed 56000 cm/s,
        // EnergyCost 3, OffsetLocations (5000, +-2500, +-2500) cm: the first; BP_Telekinesis_Debuff: CollisionDamageOtherTargets
        // 500, from geometry or another target 500000: its death), the distances as seconds of the top speed. Its Duration
        // 1e6 (held until thrown) is the remake's 8 s; the flight lasts the toss's reach at its speed and half a second.
        public const float ControlGrabTops = 70000f / 14000f, ControlTossTops = 80000f / 14000f, ControlSpeedTops = 56000f / 14000f;
        public static readonly Vector3 ControlHoldTops = new Vector3(2500f, 2500f, 5000f) / 14000f;   // (right, up, ahead)
        public const int ControlCost = 3, ControlCooldownMs = 500, ControlHoldMs = 8000;
        public static float ControlFlightMs => (ControlTossTops / ControlSpeedTops + 0.5f) * 1000f;

        // Chorus's Scan (BP_ActivePower_Scan: ShortRangeScanDistance 600000 cm, ShortRangeScanDuration 3 s, Cooldown 0; the
        // powers screen's cost 1), the reach as seconds of the top speed; how long the senses last is the remake's.
        public const float SensesTops = 600000f / 14000f;
        public const int SensesCost = 1, SensesCooldownMs = 500, SensesWaveMs = 3000, SensesMs = 20000;
        // Its long range (held: AScanActivePower's constructor 0x140c3df70 gives LongRangeChargeDuration 1 s and
        // LongRangeScanDuration 3 s, EnergyCost 1; the blueprint's LongRangeScanDistance 20000000 cm: 200 km, the whole orbit)
        public const float SensesLongTops = 20000000f / 14000f;
        public const int SensesChargeMs = 1000, SensesLongWaveMs = 3000;

        /// <summary>Whether 'at' is within the star's reach: 'radius' round the point 'ahead' along 'forward' from 'pos'.</summary>
        public static bool InStar(Vector3 pos, Vector3 forward, Vector3 at, float ahead, float radius) =>
            (at - (pos + forward.normalized * ahead)).sqrMagnitude <= radius * radius;

        /// <summary>Blink's BlinkPlayRateCurve: its timelines 1.5x as fast for a short leap, 1x at its full 1000 m ('share'
        /// of the reach; an Auto cubic between flat ends).</summary>
        public static float HuntPlayRate(float share) => Mathf.Lerp(1.5f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(share)));
        /// <summary>Blink_Disappear (0.067 s): the ship's Transition 0 -> 1, sharply at the end (its keys 0.05 at 31 %, 0.41 at
        /// 73 %: about u^3); Blink_Reappear (0.33 s): 1 -> 0 between flat ends.</summary>
        public const float HuntVanishMs = 66.667f, HuntAppearMs = 333.333f;
        public static float HuntVanish(float u) { u = Mathf.Clamp01(u); return u * u * u; }
        public static float HuntAppear(float u) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));

        /// <summary>Rite of the Hunt's landing: 'behind' metres back from the target at 'targetPos' facing 'targetForward'
        /// (ABlinkActivePower::UpdateTargetTransform: the target's rotation, BlinkLocationOffset along its forward).</summary>
        public static Vector3 HuntSpot(Vector3 targetPos, Vector3 targetForward, float behind) => targetPos - targetForward.normalized * behind;
        // EVERSPACE 2's fusion hook (BP_Device_FusionHook's defaults, cm -> m): the aim cone, the pull's delay, the end conditions
        // and the speeds; the owner's pull is (AccelerationForward cm/s^2)^1.5 / 15, 126 m/s^2 at the remake's 33 m/s^2.
        public const float HookAimDot = 0.95f, HookPullDelayMs = 300f, HookMinDistance = 200f, HookMaxAngleDot = 0.8f,
                           HookMaxSpeed = 800f, HookOwnerAccel = 126f, HookTargetAccel = 800f, HookTargetAccelRange = 200f;
        /// <summary>The corrosion's tick (BP_Condition_Debuff_Corrosion's timer).</summary>
        public const float CorrosionTickMs = 950f;
        /// <summary>The teleporter's SafetyMargin (1000 cm) before an obstacle.</summary>
        public const float BlinkMarginM = 10f;

        /// <summary>A blink of 'want' metres ahead with something 'hit' metres along the way (float.MaxValue: nothing): the way
        /// kept, 'margin' plus the ship's 'probe' radius short of it; 0 or less = no room (ES2's GetTargetOffset).</summary>
        public static float Blink(float want, float hit, float probe, float margin = BlinkMarginM) =>
            hit >= want ? want : hit - margin - probe;
        /// <summary>Shop.KindOf: the devices' one-per-ship kinds, KindBase + type (past the cruise drive's 1000).</summary>
        public const int KindBase = 1100;
        /// <summary>GoF3's cooldown for every player device (DeviceConfiguration cooldown 60).</summary>
        public const int DefaultCooldownMs = 60000;

        public enum Phase { Ready, Active, Cooldown }
        public enum Event { None, Fired, Ended, Ready }

        public readonly int item;
        public readonly Type type;
        public readonly float durationMs, cooldownMs, chargeMax;

        public Phase State { get; private set; } = Phase.Ready;
        /// <summary>Ms into the run (Active) or the cooldown.</summary>
        public float Timer { get; private set; }
        /// <summary>The strike charge's points so far (0..chargeMax).</summary>
        public float Charge { get; private set; }

        public Device(int item, Type type, int durationMs, int cooldownMs, int charge = 0)
        {
            this.item = item;
            this.type = type;
            this.durationMs = Mathf.Max(0, durationMs);
            this.cooldownMs = Mathf.Max(0, cooldownMs);
            chargeMax = Mathf.Max(0, charge);
        }

        public bool Active => State == Phase.Active;
        public bool Charged => chargeMax <= 0f || Charge >= chargeMax;
        /// <summary>The menu entry is usable (half-transparent otherwise, like the cloak's).</summary>
        public bool Available => State == Phase.Ready && Charged;
        /// <summary>0..1 toward ready: the cooldown, then the charge (1 = ready).</summary>
        public float RechargeRate => State == Phase.Cooldown ? (cooldownMs > 0f ? Mathf.Clamp01(Timer / cooldownMs) : 1f)
                                   : State == Phase.Active ? 0f : chargeMax > 0f ? Mathf.Clamp01(Charge / chargeMax) : 1f;
        /// <summary>What is left of the run, 1 at its start, 0 at its end; 0 when not running.</summary>
        public float ActiveLeft => State == Phase.Active && durationMs > 0f ? Mathf.Clamp01(1f - Timer / durationMs) : 0f;

        /// <summary>The button: false = not ready (cooling down, running or not charged).</summary>
        public bool TryFire()
        {
            if (!Available) return false;
            Charge = 0f;
            Timer = 0f;
            State = durationMs > 0f ? Phase.Active : Phase.Cooldown;
            return true;
        }

        /// <summary>Damage dealt by the ship's weapons (the strike charge); nothing while it runs.</summary>
        public void AddCharge(float points)
        {
            if (chargeMax <= 0f || State == Phase.Active || points <= 0f) return;
            Charge = Mathf.Min(chargeMax, Charge + points);
        }

        /// <summary>The device charger (EVERSPACE 2's ResetCooldowns): a cooldown ends now; true if one was running.</summary>
        public bool ResetCooldown()
        {
            if (State != Phase.Cooldown) return false;
            State = Phase.Ready;
            Timer = 0f;
            return true;
        }

        /// <summary>A death or a cutscene: a run ends now and the cooldown starts.</summary>
        public bool Cancel()
        {
            if (State != Phase.Active) return false;
            State = Phase.Cooldown;
            Timer = 0f;
            return true;
        }

        /// <summary>'skipCooldown' (remake debug, Cheats.NoEquipmentCooldown): a cooldown ends at once and the strike charge
        /// is full.</summary>
        public Event Update(float dtMs, bool skipCooldown = false)
        {
            if (dtMs <= 0f) return Event.None;
            if (skipCooldown && State != Phase.Active) Charge = chargeMax;
            switch (State)
            {
                case Phase.Active:
                    Timer += dtMs;
                    if (Timer >= durationMs) { State = Phase.Cooldown; Timer = 0f; return Event.Ended; }
                    break;
                case Phase.Cooldown:
                    Timer += skipCooldown ? cooldownMs : dtMs;
                    if (Timer >= cooldownMs) { State = Phase.Ready; Timer = 0f; return Event.Ready; }
                    break;
            }
            return Event.None;
        }

        // ---- the effects' sums ----------------------------------------------------------------------------------------

        /// <summary>The area EMP's wave front 'ms' after firing: out to 'radius' over 'growMs' (0 = at once), smoothed out
        /// at the end (GoF3's EffectZone grows at a falling rate).</summary>
        public static float WaveRadius(float ms, float growMs, float radius)
        {
            if (growMs <= 0f) return radius;
            float t = Mathf.Clamp01(ms / growMs);
            return radius * (1f - (1f - t) * (1f - t));
        }

        /// <summary>'to' (from the ship to the target) lies within 'range' and within 'coneDeg' of 'forward' (a unit vector);
        /// a cone of 180 or more takes every direction.</summary>
        public static bool InCone(Vector3 forward, Vector3 to, float range, float coneDeg)
        {
            float d = to.magnitude;
            if (d > range) return false;
            if (coneDeg >= 180f || d < 1e-6f) return true;
            return Vector3.Dot(forward, to / d) >= Mathf.Cos(coneDeg * Mathf.Deg2Rad);
        }

        /// <summary>The overcharger's cost: 'percent' of the maximum shield (GoF3: 66).</summary>
        public static int OverchargeCost(int maxShield, int percent) => Mathf.Max(0, Mathf.RoundToInt(maxShield * percent / 100f));

        /// <summary>The damage the overcharger deals per target: 'percent' of the shield spent (GoF3: 40 the area, 30 the beam).</summary>
        public static float OverchargeDamage(int cost, int percent) => cost * percent / 100f;

        /// <summary>The speed a slow leaves, 0..1, from deviceSlowPercent (GoF3's area EMP: 25 = a quarter).</summary>
        public static float SlowFactor(int percent) => Mathf.Clamp01(percent / 100f);
    }

    /// <summary>A ship slowed by an EMP device (Device.Type.AreaEmp / EmpShot: no turning either) or a slowing weapon (its
    /// slowMs / slowPercent: GoF3's AbilitySlowdown, the speed only): its speed x Factor until the time runs out. A second
    /// hit takes the longer time and the stronger slow.</summary>
    public struct Slow
    {
        public float LeftMs { get; private set; }
        float factor, noTurnMs;

        public bool Active => LeftMs > 0f;
        public float SpeedFactor => Active ? factor : 1f;
        public bool CanTurn => noTurnMs <= 0f;

        public void Apply(float ms, float speedFactor, bool noTurn = true)
        {
            if (ms <= 0f) return;
            factor = Active ? Mathf.Min(factor, Mathf.Clamp01(speedFactor)) : Mathf.Clamp01(speedFactor);
            LeftMs = Mathf.Max(LeftMs, ms);
            if (noTurn) noTurnMs = Mathf.Max(noTurnMs, ms);
        }

        public void Tick(float dtMs)
        {
            dtMs = Mathf.Max(0f, dtMs);
            if (LeftMs > 0f) LeftMs = Mathf.Max(0f, LeftMs - dtMs);
            if (noTurnMs > 0f) noTurnMs = Mathf.Max(0f, noTurnMs - dtMs);
        }

        public void Clear() { LeftMs = 0f; noTurnMs = 0f; }
    }
}
