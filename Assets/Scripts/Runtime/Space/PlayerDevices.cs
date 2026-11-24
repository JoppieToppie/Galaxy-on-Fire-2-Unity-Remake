// PlayerDevices.cs
// Remake: the player ship's active devices (Flight.Device; Modding/README.md "Devices"), Galaxy on Fire 3's: every mounted
// item with the device attribute (150), in mount order. Each is fired from the quick menu (Navigation's Device entries,
// half-transparent while not ready) or its key (GameControls.Device1-3: the first three), runs for deviceDurationMs, then
// cools down (GoF3: 60 s) and says "{0} ready". CooldownView shows each beside the cloak's.
// The effects (PlayerDevicesEffects): the EMP wave and shots (Target.SlowBy), the overcharger's shield spent as damage,
// the strike charge's damage boost (WeaponSystem.DamageBoost, charged by PrimaryDamageDealt) and the deflector
// (Target.Deflects). Damage goes through WeaponSystem.DeviceHit, so kills, standing and medals count as the player's.
// Remake (2026-11-02): EVERSPACE 2's consumables as devices 8-12 (decompiled: BP_Consumable_SmokeBomb with
// BP_SmokeBombFIeld and its SmokeScreen debuff, BP_Consumable_ArmorRepair, _NanoInjector, _SecondariesRestock,
// _DeviceCharger; the ES2 importer's items): a consumable device (deviceConsumable) is one use per unit, its mounted stack's
// amount the uses left (the hangar stacks bought ones like missiles), "{0}: {1} left" after each; the last use takes the
// item off the ship. The sensor cloud hides the ship from the enemies while it flies inside it (Target.cloaked, as the
// cloak does: no fire, missiles shaken off); the repairs and the restock are instant; the charger ends the other
// devices' cooldowns.
// Remake (2026-11-13): Chorus's Rites (a device with deviceVoidCost > 0, Rite of the Hunt first) also pay that many pips of the
// ship's Void energy (Flight.VoidEnergy, made when one is mounted: 3 pips, one back every 4.5 s); a Rite is ready only with
// the pips there ("{0}: not enough Void energy"), and its HUD sweep is the pool's fill toward its cost. The pips grow back
// on Void Crystals from the hold (VoidEnergy.Burn: 1 t a pip; none aboard, none grow: "No Void Crystals aboard").

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public partial class PlayerDevices : MonoBehaviour
    {
        public sealed class Entry
        {
            public ItemData item;
            public string name;
            public Device rules;
            /// <summary>The item's numbers (radius in game units, percentages as given).</summary>
            public int radius, growMs, slowPercent, shieldCost, damagePercent, coneDeg, damage, emp;
            /// <summary>Remake: a consumable (one use per unit, deviceConsumable) and the restock's rounds (deviceAmmo).</summary>
            public bool consumable;
            public int ammo, range2;
            /// <summary>Remake: a Rite's Void energy cost (deviceVoidCost; 0 = not a Rite).</summary>
            public int voidCost;
            /// <summary>Remake: the Rite of the Storm's jam and the Senses' length (their deviceDurationMs: the devices themselves
            /// are instant), the Storm's jumps.</summary>
            public int jamMs, chain;
            /// <summary>Remake: a Rite's upgrades (deviceUpgrade: Device.UpgradeA / UpgradeB bits).</summary>
            public int upgrade;
            public bool A => (upgrade & Device.UpgradeA) != 0;
            public bool B => (upgrade & Device.UpgradeB) != 0;
            /// <summary>Available last frame: "{0} ready" when it turns true (the cooldown's end, the strike charge filled).</summary>
            public bool wasAvailable = true;
        }

        readonly List<Entry> devices = new List<Entry>();
        // Remake: the passive Rites (Device.Type.RitePassive): mounted, never fired; their pips and immunity
        int voidPips;
        bool hasRite, elementalProof;
        public IReadOnlyList<Entry> Devices => devices;
        /// <summary>Remake: the Void energy the Rites pay from, null without a Rite mounted (3 pips and the passives' more).</summary>
        public VoidEnergy VoidPool { get; private set; }
        /// <summary>A HUD message ("{0} ready", not enough shield, not charged).</summary>
        public event Action<string> Message;
        /// <summary>No device may be fired now (the launch camera, a jump, a cutscene, the guns blocked).</summary>
        public Func<bool> Blocked;

        Database db;
        Target self;
        WeaponSystem weapons;
        CombatAssets assets;
        AudioSource sfx;
        // the effects (PlayerDevicesEffects)
        Transform fxRoot;
        readonly Dictionary<Entry, Shot> shots = new Dictionary<Entry, Shot>();
        readonly Dictionary<int, Shot> deflected = new Dictionary<int, Shot>();   // by the shooter's gun item
        readonly List<Wave> waves = new List<Wave>();
        readonly List<(Transform beam, Target from, Target to, float ms)> beams = new List<(Transform, Target, Target, float)>();
        readonly List<Transform> beamPool = new List<Transform>();
        Func<Gun, int, bool> deflect;
        Entry deflector;
        static readonly Func<InputAction>[] Keys = { () => GameControls.Device1, () => GameControls.Device2, () => GameControls.Device3 };

        /// <summary>The ship's devices, null without any.</summary>
        public static PlayerDevices Attach(GameObject player, Database db, Target self, WeaponSystem weapons)
        {
            if (db == null || self == null) return null;
            List<ItemData> items = null;
            var kinds = new HashSet<int>();
            foreach (var e in Session.Equipment)
            {
                var it = db.Item(e.item);
                if (!Shop.IsDevice(it) || !kinds.Add(Shop.KindOf(it))) continue;   // one per type (Shop.CanInstallMultiple)
                (items ??= new List<ItemData>()).Add(it);
            }
            if (items == null) return null;
            var c = player.AddComponent<PlayerDevices>();
            c.db = db;
            c.self = self;
            c.weapons = weapons;
            foreach (var it in items) c.Add(it);
            if (c.hasRite) c.VoidPool = new VoidEnergy(VoidEnergy.ChorusMax + c.voidPips);
            Hazards.ElementalProof = c.elementalProof;
            c.Setup();
            return c;
        }

        void Add(ItemData it)
        {
            var type = Shop.DeviceType(it);
            if (type == Device.Type.RitePassive)
            {
                voidPips += Mathf.Max(0, it.Attr(Device.VoidPipsAttr));
                elementalProof |= it.Attr(Device.ElementalAttr) > 0;
                return;
            }
            bool consumable = Shop.IsConsumableDevice(it);
            bool hunt = type == Device.Type.RiteHunt, storm = type == Device.Type.RiteStorm, star = type == Device.Type.RiteStar,
                 control = type == Device.Type.RiteControl, senses = type == Device.Type.RiteSenses;
            var e = new Entry
            {
                item = it,
                name = GameNames.Item(it.index),
                // (a consumable has no cooldown unless given one: its uses are the limit)
                rules = new Device(it.index, type, storm || senses ? 0 : it.Attr(Device.DurationAttr, star ? Device.StarMaxMs : control ? Device.ControlHoldMs : 0),
                                   it.Attr(Device.CooldownAttr, consumable ? 0 : hunt ? Device.HuntCooldownMs : storm ? Device.StormCooldownMs
                                                                  : star ? Device.StarCooldownMs : control ? Device.ControlCooldownMs
                                                                  : senses ? Device.SensesCooldownMs
                                                                  : Device.DefaultCooldownMs),
                                   type == Device.Type.StrikeCharge ? it.Attr(Device.ChargeAttr) : 0),
                consumable = consumable,
                ammo = it.Attr(Device.AmmoAttr),
                range2 = it.Attr(Device.Range2Attr),
                voidCost = it.Attr(Device.VoidCostAttr, hunt ? Device.HuntCost : storm ? Device.StormCost : star ? Device.StarCost
                                                        : control ? Device.ControlCost : senses ? Device.SensesCost : 0),
                jamMs = storm ? it.Attr(Device.DurationAttr, Device.StormJamMs) : senses ? it.Attr(Device.DurationAttr, Device.SensesMs) : 0,
                upgrade = it.Attr(Device.UpgradeAttr),
                chain = it.Attr(Device.ChainAttr, storm && (it.Attr(Device.UpgradeAttr) & Device.UpgradeA) != 0 ? Device.StormChainA : 0),
                radius = it.Attr(Device.RadiusAttr),
                growMs = it.Attr(Device.GrowAttr, storm ? Device.StormChainMs : star ? Device.StarTickMs : 0),
                slowPercent = it.Attr(Device.SlowAttr, storm ? 0 : 25),
                shieldCost = it.Attr(Device.ShieldCostAttr),
                damagePercent = it.Attr(Device.DamagePercentAttr, 100),
                coneDeg = it.Attr(Device.ConeAttr, 30),
                damage = it.Attr(9),
                emp = it.Attr(10),
            };
            devices.Add(e);
            if (e.voidCost > 0) hasRite = true;
        }

        /// <summary>Whether 'e' can be fired now: its own rules, and a Rite's energy.</summary>
        public bool Ready(Entry e) => e.rules.Available && (e.voidCost <= 0 || VoidPool == null || Cheats.NoEquipmentCooldown || VoidPool.CanPay(e.voidCost));

        /// <summary>The HUD's sweep for 'e': its cooldown, a ready Rite's the pool's fill toward its cost.</summary>
        public float Charge(Entry e) =>
            e.rules.State == Device.Phase.Ready && e.voidCost > 0 && VoidPool != null ? VoidPool.ChargeToward(e.voidCost) : e.rules.RechargeRate;

        void Setup()
        {
            assets = CombatAssets.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            if (weapons != null) weapons.PrimaryDamageDealt += OnDamageDealt;
            if (VoidPool != null)
            {
                // the regrowth's fuel (the user's choice): Void Crystals from the hold; the debug's free equipment needs none
                VoidPool.HasFuel = () => Cheats.NoEquipmentCooldown || Shop.CargoOf(VoidEnergy.VoidCrystalItem) >= VoidEnergy.CrystalsPerPip;
                VoidPool.Burn = () => { if (!Cheats.NoEquipmentCooldown) Shop.RemoveFromCargo(VoidEnergy.VoidCrystalItem, VoidEnergy.CrystalsPerPip); };
            }
            SetupEffects();
        }

        void OnDestroy()
        {
            if (weapons != null) { weapons.PrimaryDamageDealt -= OnDamageDealt; weapons.DamageBoost = 1f; }
            Hazards.ElementalProof = false;
            if (self != null && (object)self.Deflects == (object)deflect) self.Deflects = null;
            DestroyEffects();
        }

        void OnDamageDealt(float damage)
        {
            foreach (var e in devices) e.rules.AddCharge(damage);
        }

        /// <summary>The quick menu's entry / the key for device 'slot' (mount order).</summary>
        public void Use(int slot) => Use(slot, false);

        /// <summary>'longRange': the Rite of the Senses' long range (its key held).</summary>
        public void Use(int slot, bool longRange)
        {
            senseLong = longRange;
            if (slot < 0 || slot >= devices.Count || self == null || !self.Alive) return;
            if (Time.timeScale <= 0f || (Blocked != null && Blocked())) return;
            var e = devices[slot];
            // Remake (Chorus's Drill: MinDurationTime 0.5): a second press on a running Star ends it, after its first 0.5 s
            if (e.rules.type == Device.Type.RiteStar && e.rules.Active)
            {
                if (e.rules.Timer >= Device.StarMinMs && e.rules.Cancel()) End(e);
                return;
            }
            // Remake (Chorus's Telekinesis): a second press on a held ship throws it
            if (e.rules.type == Device.Type.RiteControl && e.rules.Active)
            {
                Toss(e);
                if (e.rules.Cancel()) End(e);
                return;
            }
            if (e.rules.State == Device.Phase.Ready && !e.rules.Charged)
            {
                Message?.Invoke(string.Format(Localization.Extra("deviceNoCharge", "{0}: not charged yet ({1} %)"), e.name,
                                              Mathf.FloorToInt(e.rules.RechargeRate * 100f)));
                return;
            }
            if (!e.rules.Available) return;
            bool payVoid = e.voidCost > 0 && VoidPool != null && !Cheats.NoEquipmentCooldown;   // remake debug: Chorus's InfiniteVoidEnergy
            if (payVoid && !VoidPool.CanPay(e.voidCost))
            {
                Message?.Invoke(string.Format(Localization.Extra("deviceNoVoidEnergy", "{0}: not enough Void energy"), e.name));
                return;
            }
            if (!CanFire(e)) return;
            if (!e.rules.TryFire()) return;
            if (payVoid) VoidPool.TryPay(e.voidCost);
            Fire(e);
            if (e.consumable) Spend(e);
        }

        /// <summary>Remake: a consumable device's use spent from its mounted stack; the last one takes the item off the ship
        /// (its run, if any, still ends on its own).</summary>
        void Spend(Entry e)
        {
            var stack = Session.Equipment.Find(s => s.item == e.item.index);
            if (stack == null) return;
            stack.amount = Mathf.Max(0, stack.amount - 1);
            if (stack.amount > 0)
            {
                Message?.Invoke(string.Format(Localization.Extra("deviceUsesLeft", "{0}: {1} left"), e.name, stack.amount));
                return;
            }
            Message?.Invoke(string.Format(Localization.Extra("deviceUsedUp", "{0}: used up"), e.name));
            Hangar.PinSlots(db, Session.Equipment);
            Session.Equipment.Remove(stack);
            if (!e.rules.Active) devices.Remove(e);
            else spent.Add(e);   // dropped when its run ends (the sensor cloud)
        }

        readonly List<Entry> spent = new List<Entry>();
        bool voidStarved;
        // the Senses' key held (its slot, -1 = none) and for how long; the next fire's range
        int senseKey = -1;
        float senseHeldMs;
        bool senseLong;

        /// <summary>A death, a cutscene or a jump: every running device stops and cools down.</summary>
        public void Cancel()
        {
            foreach (var e in devices) if (e.rules.Cancel()) End(e);
            EndHunt();   // (a leap under way: the model back, no leap)
            stormLeft = 0;
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;
            if (dtMs <= 0f) return;   // paused
            for (int i = 0; i < Keys.Length && i < devices.Count; i++)
            {
                var key = Keys[i]();
                if (key == null) continue;
                if (devices[i].rules.type == Device.Type.RiteSenses)
                {
                    // Remake (Chorus's Scan): a tap scans close by; held SensesChargeMs, the long range
                    if (key.WasPressedThisFrame()) { senseKey = i; senseHeldMs = 0f; }
                    if (senseKey != i) continue;
                    if (key.IsPressed())
                    {
                        senseHeldMs += Time.unscaledDeltaTime * 1000f;
                        if (senseHeldMs >= Device.SensesChargeMs) { senseKey = -1; Use(i, true); }
                    }
                    else { senseKey = -1; Use(i); }
                    continue;
                }
                if (key.WasPressedThisFrame()) Use(i);
            }
            for (int i = devices.Count - 1; i >= 0; i--)
            {
                var e = devices[i];
                if (e.rules.Update(dtMs, Cheats.NoEquipmentCooldown) == Device.Event.Ended)   // remake debug: no cooldown
                {
                    End(e);
                    if (spent.Remove(e)) { devices.RemoveAt(i); continue; }   // the last use of a consumable, now over
                }
                bool available = Ready(e);
                if (available && !e.wasAvailable) Message?.Invoke(string.Format(Localization.Extra("deviceReady", "{0} ready"), e.name));
                e.wasAvailable = available;
            }
            if (VoidPool != null)
            {
                VoidPool.Step(dtMs / 1000f);
                if (VoidPool.Starved && !voidStarved)
                    Message?.Invoke(string.Format(Localization.Extra("voidNoCrystals", "No {0} aboard: the Void energy can't grow back"),
                                                  GameNames.Item(VoidEnergy.VoidCrystalItem)));
                voidStarved = VoidPool.Starved;
            }
            UpdateEffects(dtMs);
        }

        void Play(AudioClip clip)
        {
            if (clip != null) sfx.PlayOneShot(Modding.ModSounds.Get(clip), Settings.SfxVolume);
        }

        /// <summary>The ships a device acts on: hostile, alive, not phased or out of the player's reach.</summary>
        bool Affects(Target t) =>
            (object)t != null && t != self && t.isShip && !t.isPlayer && t.hostileToPlayer && t.Alive && !t.phased && !t.playerProof && t.isActiveAndEnabled;
    }
}
