// NpcDevice.cs
// Remake (2026-11-04, for mods, after Galaxy on Fire 3's boss devices): a device on an NPC ship, a Most Wanted criminal's
// (wanted.json "devices", Modding/README.md "Most Wanted"; World.NpcDevices runs it). GoF3 (DeviceSystem / DeviceAgentSystem;
// data.pdb's Device: a DeviceConfiguration* with duration and cooldown, a DeviceBehaviorConfigurationUniversal saying when):
// a device is used when it is ready and its triggers say so. The remake reads GoF3's triggers this way: the events (on a hit
// "onHitUseEnabled", on an interval "intervalUseEnabled" x "intervalProbability") fire it, any one of them; the conditions
// (the player within "playerInRangeMin/MaxDist", within or outside an angle of the ship's nose "playerInAngle" /
// "playerOutAngle", the player's nose on the ship "playerAlignmentAngle") must all hold; with no event it fires as soon as the
// conditions hold. A run lasts its duration, then the cooldown counts (GoF3's cooldown runs from the use: the remake from the
// end, as the player's devices do, Flight.Device).

namespace GoF2Remake.Flight
{
    public class NpcDeviceSpec
    {
        public enum Kind { Teleport, FrontShield, BackShield, EnergyAbsorber, RadarJammer, FlashGrenade, MindControl, BombSpreader, MineLayer, Cloud, ShadowClone, Minions, Wormhole }

        public Kind kind;
        public float durationMs = 5000f, cooldownMs = 15000f;
        // the triggers: events (onHit, everyMs x chance) and conditions (0 = off)
        public bool onHit;
        public float everyMs, chance = 1f;
        public float rangeMin, rangeMax;           // game units, the player's distance
        public float aheadDeg, notAheadDeg;        // the player within / outside this angle of the ship's nose
        public float facingDeg;                    // the player's nose within this angle of the ship
        public float hullBelow;                    // the ship's own hull at or under this share (0..1; 0 = off): a phase
                                                   // (EVERSPACE 2's bosses on their shield / armour depleted)
        // the effects
        public float delayMs;                      // FlashGrenade: from the throw to the flash
        public float strength = 1f;                // FlashGrenade: the flash's opacity looking straight at it (0..1)
        public float distanceMin = 1200f, distanceMax = 2400f;   // Teleport: from the player, game units
        public float blockShare = 1f;              // the shields: the share of a hit stopped
        public float shieldDeg = 90f;              // the shields: hits coming within this angle of the nose (the tail) are stopped
        public float healShare = 0.5f;             // EnergyAbsorber: the hull mended by this share of what it soaks
        public float bombEveryMs = 1200f;          // BombSpreader, MineLayer: a mine this often while it runs (the first at the start)
        public float damage = 350f;                // BombSpreader, MineLayer: a mine's blast (GoF2's AMR Saber)
        // MineLayer: GoF3's boss mine layers (ArmingSmartBomb; its mine a MovingBomb, Mine_04_A01_UB01_D'rak's numbers, GoF3's
        // distances x 11.4): a mine 'layBehind' units behind the ship chases the player within 'seekRange' at 'seekSpeed'
        // (BasicMoveToTarget), goes off within 'triggerRadius' or after 'lifeMs', blasting 'blastRadius' for 'damage'
        // (slowing the ship by 'slowShare' for 'slowMs': the layer's AbilitySlowdown); 'hull' points shoot it down
        public float hull = 60f, lifeMs = 5000f, seekSpeed = 40f * 11.4f, seekRange = 1000f * 11.4f;
        public float triggerRadius = 85f * 11.4f, blastRadius = 170f * 11.4f, layBehind = 75f * 11.4f;
        public float slowMs, slowShare;
        // Cloud: GoF3's smart bombs' effect zones (EffectZone: basicEffectZoneRadius, Lifetime, applyBuffsOnEnter; Mudshark's
        // EffectZoneNanobite_Mudshark_Boss_Weapon): a cloud of 'cloudRadius' units laid 'layBehind' its tail (like a mine,
        // every 'bombEveryMs' while it runs) lasts 'lifeMs'; inside it the player's ship keeps 1 - 'slowShare' of its speed
        // and loses 'hullPercent' of its hull a second (BuffDamageOverTime's damagePercentagePerInterval)
        public float cloudRadius = 500f * 11.4f, hullPercent;
        // Cloud: also 'shieldPercent' of the player's shield a second (GoF3's sphere mines: BuffShieldDamageOverTime)
        public float shieldPercent;
        // Who carries it (GoF3's SW_DL_* loadouts by danger level): the player's rank within minRank..maxRank (0 = no
        // bound) and 'share' of the hull's NPCs, rolled as each one spawns (NpcDevices.Attach)
        public int minRank, maxRank;
        public float share = 1f;

        /// <summary>This NPC carries it: the rank in range and 'roll01' (0..1) within its share.</summary>
        public bool Carried(int rank, float roll01) =>
            (minRank <= 0 || rank >= minRank) && (maxRank <= 0 || rank <= maxRank) && roll01 < share;
        // ShadowClone: GoF3's DeviceConfigurationCloning (Khador: cloneCount 2, Ship_A02_BB01_ONLY_FOR_CLONING with 21 % of its
        // hull + shield): 'count' copies of the ship with 'hullShare' of its hull, flying and firing as it does until shot
        // down; gone when the run ends or the ship dies. Minions: DeviceConfigurationShieldingMinions (Sh'gaal: minionCount 5,
        // minRadius 100, a Minion of 300 hull, 7 % of its own): 'count' drones of 'hullShare' of its hull circling it 'radius'
        // units out; while any lives the ship's hits lose 'blockShare' (the drones' shield on it); gone when the run ends
        public int count = 2;
        public float hullShare = 0.2f, radius = 100f * 11.4f;
        // Wormhole: GoF3's DeviceConfigurationDynamicWormholeGenerator (Khador's; DynamicWormholeEntity: Lifetime 5 s,
        // wormholeRadius 60, exitDistanceFromOwner 150, spawned at the owner's (0, 0, 50): 50 behind it): a hole of 'radius'
        // 'layBehind' its tail for 'lifeMs'; the player's ship flying in comes out 'exitDistance' in front of its nose,
        // heading as it was (in its sights)
        public float exitDistance = 150f * 11.4f;

        /// <summary>It drops mines (or clouds) while it runs (DueBombs).</summary>
        public bool Lays => kind == Kind.BombSpreader || kind == Kind.MineLayer || kind == Kind.Cloud;

        public bool HasEvent => onHit || everyMs > 0f;
    }

    /// <summary>One device's run: Step says when it starts.</summary>
    public class NpcDeviceState
    {
        public readonly NpcDeviceSpec spec;
        float activeMs, cooldownMs, intervalMs, delayMs, bombMs;

        public NpcDeviceState(NpcDeviceSpec spec)
        {
            this.spec = spec;
            intervalMs = spec.everyMs;
        }

        public bool Active => activeMs > 0f;
        public bool Ready => activeMs <= 0f && cooldownMs <= 0f;
        /// <summary>FlashGrenade: thrown, the flash still to come (ms).</summary>
        public bool Pending => delayMs > 0f;
        /// <summary>BombSpreader, MineLayer: the mines to drop after the last Step.</summary>
        public int DueBombs { get; private set; }

        /// <summary>What the ship sees of the player this frame (Step's conditions).</summary>
        public struct View
        {
            public bool hit;              // the ship was hit since the last step
            public bool playerNear;       // a player to use it on at all (alive, in the orbit)
            public float distance;        // game units
            public float offNoseDeg;      // the player's direction against the ship's nose
            public float playerFacingDeg; // the player's nose against the direction to the ship
            public float hull;            // the ship's hull share left (0..1)
        }

        /// <summary>Advances 'dtMs'; true when the device starts now ('random01' a roll in [0, 1) for the interval's chance).
        /// 'flashed' turns true on the step a pending flash goes off.</summary>
        public bool Step(float dtMs, in View v, float random01, out bool flashed)
        {
            flashed = false;
            DueBombs = 0;
            if (delayMs > 0f && (delayMs -= dtMs) <= 0f) flashed = true;
            if (activeMs > 0f)
            {
                if (spec.Lays && spec.bombEveryMs > 0f)
                    for (bombMs -= dtMs; bombMs <= 0f && activeMs - dtMs > 0f; bombMs += spec.bombEveryMs) DueBombs++;
                if ((activeMs -= dtMs) <= 0f) cooldownMs = spec.cooldownMs;
                return false;
            }
            if (cooldownMs > 0f) cooldownMs -= dtMs;
            bool tick = false;
            if (spec.everyMs > 0f && (intervalMs -= dtMs) <= 0f) { intervalMs += spec.everyMs; tick = random01 < spec.chance; }
            if (cooldownMs > 0f || !v.playerNear) return false;
            bool evt = !spec.HasEvent || spec.onHit && v.hit || tick;
            if (!evt || !Conditions(v)) return false;
            activeMs = System.Math.Max(1f, spec.durationMs);
            if (spec.kind == NpcDeviceSpec.Kind.FlashGrenade) delayMs = System.Math.Max(1f, spec.delayMs);
            if (spec.Lays) { DueBombs = 1; bombMs = spec.bombEveryMs; }
            return true;
        }

        bool Conditions(in View v)
        {
            if (spec.rangeMax > 0f && v.distance > spec.rangeMax) return false;
            if (spec.rangeMin > 0f && v.distance < spec.rangeMin) return false;
            if (spec.aheadDeg > 0f && v.offNoseDeg > spec.aheadDeg) return false;
            if (spec.notAheadDeg > 0f && v.offNoseDeg <= spec.notAheadDeg) return false;
            if (spec.facingDeg > 0f && v.playerFacingDeg > spec.facingDeg) return false;
            if (spec.hullBelow > 0f && v.hull > spec.hullBelow) return false;
            return true;
        }

        /// <summary>The shields: the share of a hit coming from 'fromDeg' off the ship's nose (0 = head on) that gets through.</summary>
        public float ShieldPass(float fromDeg)
        {
            if (!Active) return 1f;
            bool covered = spec.kind == NpcDeviceSpec.Kind.FrontShield ? fromDeg <= spec.shieldDeg
                         : spec.kind == NpcDeviceSpec.Kind.BackShield && fromDeg >= 180f - spec.shieldDeg;
            return covered ? 1f - spec.blockShare : 1f;
        }

        /// <summary>The flash's opacity for a player whose nose is 'facingDeg' off the ship: full looking straight at it, none
        /// past 90 degrees (GoF3: "turn their ship away from the flash to lessen any damage").</summary>
        public float FlashOpacity(float facingDeg) =>
            facingDeg >= 90f ? 0f : spec.strength * (float)System.Math.Cos(facingDeg * System.Math.PI / 180.0);

        public void Reset() { activeMs = cooldownMs = delayMs = bombMs = 0f; intervalMs = spec.everyMs; DueBombs = 0; }
    }
}
