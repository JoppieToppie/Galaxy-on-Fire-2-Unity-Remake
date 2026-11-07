// WingmanDevice.cs
// Remake (2026-11-05, for mods, after Galaxy on Fire 3's wingmen): the device a hired wingman's pilot uses, by the hull it
// flies (ships.json "wingmanDevice"; Modding/README.md "Wingmen"; World.NpcShip runs it). GoF3 gives every hull one of three
// (Wingman.wingmanDevice; data.pdb's DeviceConfigurationWingman*, cooldown 55 s each):
//   Hull Repair    DeviceConfigurationWingmanHullRepair: with the leader's hull under 40 % (hullRepairActivationPercentage),
//                  3 s of 0.278 % every 1/30 s (hullRepairPercentagePerInterval / hullRepairIntervalSec): 25 % of it
//   Ammo Supply    DeviceConfigurationWingmanAmmoRefill: the leader's ammunition refilled (GoF3: its secondary's magazine;
//                  the remake: AmmoRounds more of the selected secondary once it is down to AmmoBelow or less)
//   Charged Shots  DeviceConfigurationWingmanOverheatingProjectiles: the wingman's own shots x1.75 for 5 s
//                  (damageBoostMultiplier), when it is about to attack (waitsForPlayerAboutToAttack: the remake, while it
//                  attacks)
// The cooldown runs from the end of a use, as the player's devices' (Flight.Device).

namespace GoF2Remake.Flight
{
    public class WingmanDevice
    {
        public enum Kind { None, Repair, Ammo, Charged }

        public const float CooldownMs = 55000f, RepairMs = 3000f, ChargedMs = 5000f, AmmoMs = 1000f;
        public const float RepairBelow = 0.4f, RepairShare = 0.25f, ChargedFactor = 1.75f;
        public const int AmmoBelow = 1, AmmoRounds = 5;

        public readonly Kind kind;
        float activeMs, cooldownMs;

        public WingmanDevice(Kind kind) { this.kind = kind; }

        /// <summary>ships.json "wingmanDevice": "repair", "ammo", "charged"; anything else (none) = None.</summary>
        public static Kind Parse(string word)
        {
            switch ((word ?? "").Trim().ToLowerInvariant())
            {
                case "repair": return Kind.Repair;
                case "ammo": return Kind.Ammo;
                case "charged": return Kind.Charged;
                default: return Kind.None;
            }
        }

        public static bool Valid(string word) => Parse(word) != Kind.None;

        public bool Active => activeMs > 0f;
        public bool Ready => activeMs <= 0f && cooldownMs <= 0f;

        /// <summary>The wingman's gun damage x this (Charged Shots while it runs).</summary>
        public float DamageFactor => kind == Kind.Charged && Active ? ChargedFactor : 1f;

        /// <summary>Whether its trigger holds: 'hullShare' the leader's hull and armor left (0..1), 'ammo' the leader's
        /// selected secondary's rounds (-1 = none selected), 'attacking' the wingman has a target.</summary>
        public bool Wanted(float hullShare, int ammo, bool attacking)
        {
            switch (kind)
            {
                case Kind.Repair: return hullShare > 0f && hullShare < RepairBelow;
                case Kind.Ammo: return ammo >= 0 && ammo <= AmmoBelow;
                case Kind.Charged: return attacking;
                default: return false;
            }
        }

        /// <summary>One frame: true when a use starts now ('wanted' and ready). 'repairShare' is the share of the leader's
        /// hull to mend this frame (Hull Repair while it runs, RepairShare over RepairMs in all).</summary>
        public bool Step(float dtMs, bool wanted, out float repairShare)
        {
            repairShare = 0f;
            if (kind == Kind.None) return false;
            if (activeMs > 0f)
            {
                float run = activeMs < dtMs ? activeMs : dtMs;
                if (kind == Kind.Repair) repairShare = RepairShare * run / RepairMs;
                activeMs -= dtMs;
                if (activeMs <= 0f) { activeMs = 0f; cooldownMs = CooldownMs; }
                return false;
            }
            if (cooldownMs > 0f) { cooldownMs -= dtMs; return false; }
            if (!wanted) return false;
            activeMs = kind == Kind.Repair ? RepairMs : kind == Kind.Charged ? ChargedMs : AmmoMs;
            return true;
        }
    }
}
