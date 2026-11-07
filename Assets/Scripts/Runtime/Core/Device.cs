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
        }

        public const int TypeCount = 7;
        /// <summary>The remake attributes (Modding.ItemStats).</summary>
        public const int TypeAttr = 150, CooldownAttr = 151, DurationAttr = 152, RadiusAttr = 153, GrowAttr = 154,
                         SlowAttr = 155, ShieldCostAttr = 156, DamagePercentAttr = 157, ChargeAttr = 158, ConeAttr = 159;
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
