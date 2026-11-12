// Cloak.cs
// The player's cloaking device as plain rules (Reference/research/combat_equipment.md 1): items 94-96 (sort 21) or the
// integrated cloak of ships 44 Specter / 49 Scimitar (items[95]'s stats).
//   PlayerEgo::toggleCloaking 0xa65a4   ready + enough energy cells (item 122, attr 38) -> pay, charge for attr 36 ms
//   PlayerEgo::update 0xa941a..0xa98e0  charge done -> cloaked for attr 35 ms (Player+0x5e untargetable); 2000 ms fade in
//                                       and out; the end starts the cooldown (PlayerEgo+0x368: Easy 5000 ms, Normal 7000,
//                                       Hard 9000, Extreme 12000);
//                                       cooldown over -> "Cloak ready" (316)
//   getCloakingPercentage 0xa81a0       0..100 over the first / last 2000 ms, 100 between
// Nothing ends the cloak early (firing, hits, boosting and locks don't touch it). Every level starts ready.
// Remake option (Settings.EmpBreaksCloak, on by default): an EMP hit does (Break).
// Remake mods: cloakCooldownMs (attribute 160) gives an item its own cooldown (GoF3's Hiroto Cloak: 60 s).

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class Cloak
    {
        public enum Phase { Ready, Charging, Cloaked, Cooldown }

        public const int EnergyCellItem = 122, FadeMs = 2000;

        public readonly int item, durationMs, chargeMs, cells;
        public readonly float cooldownMs;

        public Phase State { get; private set; } = Phase.Ready;
        /// <summary>PlayerEgo+0x208: ms into the current charge / cloak.</summary>
        public float Timer { get; private set; }
        /// <summary>PlayerEgo+0x20c: cooldown left (ms).</summary>
        public float CooldownLeft { get; private set; }

        /// <summary>Remake attribute 160 (Modding.ItemStats "cloakCooldownMs"): the item's own cooldown.</summary>
        public const int CooldownAttr = 160;

        /// <summary>'cooldownOverrideMs' > 0: the item's own cooldown (attribute 160) in place of the difficulty's.</summary>
        public Cloak(int item, int durationMs, int chargeMs, int cells, float difficulty, int cooldownOverrideMs = 0)
        {
            this.item = item;
            this.durationMs = Mathf.Max(durationMs, 2 * FadeMs);
            this.chargeMs = chargeMs;
            this.cells = cells;
            cooldownMs = cooldownOverrideMs > 0 ? cooldownOverrideMs
                       : difficulty <= 0f ? 5000f : difficulty <= 0.6f ? 7000f : difficulty <= 1.1f ? 9000f : 12000f;
        }

        public bool Cloaked => State == Phase.Cloaked;
        /// <summary>The menu entry is usable (Hud::initHudMenu: half-transparent while cloaked / charging / recharging).</summary>
        public bool Available => State == Phase.Ready;
        public float ChargeRate => State == Phase.Charging ? Mathf.Clamp01(Timer / chargeMs) : 0f;
        /// <summary>getCloakRechargeRate: 1 - cooldownLeft / cooldownLength (the entry's fill while recharging).</summary>
        public float RechargeRate => State == Phase.Cooldown ? 1f - CooldownLeft / cooldownMs : 1f;
        /// <summary>What is left of the cloak while cloaked, 1 at its start, 0 when it ends.</summary>
        public float CloakLeft => State == Phase.Cloaked && durationMs > 0 ? Mathf.Clamp01(1f - Timer / durationMs) : 0f;

        /// <summary>getCloakingPercentage.</summary>
        public float Percentage
        {
            get
            {
                if (State != Phase.Cloaked) return 0f;
                if (Timer < FadeMs) return Timer * 100f / FadeMs;
                if (Timer > durationMs - FadeMs) return Mathf.Max(0f, (1f - (Timer - (durationMs - FadeMs)) / FadeMs) * 100f);
                return 100f;
            }
        }

        /// <summary>toggleCloaking from the ready state: false = not enough cells (583). The caller removes the cells.</summary>
        public bool TryStart(int cellsInCargo)
        {
            if (State != Phase.Ready || cellsInCargo < cells) return false;
            State = Phase.Charging;
            Timer = 0f;
            return true;
        }

        /// <summary>Remake (Settings.EmpBreaksCloak): an EMP hit ends a charge or the cloak at once and the cooldown starts;
        /// false = nothing to break.</summary>
        public bool Break()
        {
            if (State != Phase.Charging && State != Phase.Cloaked) return false;
            State = Phase.Cooldown;
            Timer = 0f;
            CooldownLeft = cooldownMs;
            return true;
        }

        public enum Event { None, Engaged, Ended, Ready }

        /// <summary>'skipCooldown' (remake debug, Cheats.NoEquipmentCooldown): a cooldown ends at once.</summary>
        public Event Update(float dtMs, bool skipCooldown = false)
        {
            switch (State)
            {
                case Phase.Charging:
                    Timer += dtMs;
                    if (Timer > chargeMs) { State = Phase.Cloaked; Timer = 0f; return Event.Engaged; }
                    break;
                case Phase.Cloaked:
                    Timer += dtMs;
                    if (Timer > durationMs) { State = Phase.Cooldown; Timer = 0f; CooldownLeft = cooldownMs; return Event.Ended; }
                    break;
                case Phase.Cooldown:
                    CooldownLeft -= skipCooldown ? CooldownLeft : dtMs;
                    if (CooldownLeft <= 0f) { CooldownLeft = 0f; State = Phase.Ready; return Event.Ready; }
                    break;
            }
            return Event.None;
        }
    }
}
