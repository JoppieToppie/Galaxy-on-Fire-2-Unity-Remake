// PlayerDevices.cs
// Remake: the player ship's active devices (Flight.Device; Modding/README.md "Devices"), Galaxy on Fire 3's: every mounted
// item with the device attribute (150), in mount order. Each is fired from the quick menu (Navigation's Device entries,
// half-transparent while not ready) or its key (GameControls.Device1-3: the first three), runs for deviceDurationMs, then
// cools down (GoF3: 60 s) and says "{0} ready". CooldownView shows each beside the cloak's.
// The effects (PlayerDevicesEffects): the EMP wave and shots (Target.SlowBy), the overcharger's shield spent as damage,
// the strike charge's damage boost (WeaponSystem.DamageBoost, charged by PrimaryDamageDealt) and the deflector
// (Target.Deflects). Damage goes through WeaponSystem.DeviceHit, so kills, standing and medals count as the player's.

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
            /// <summary>Available last frame: "{0} ready" when it turns true (the cooldown's end, the strike charge filled).</summary>
            public bool wasAvailable = true;
        }

        readonly List<Entry> devices = new List<Entry>();
        public IReadOnlyList<Entry> Devices => devices;
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
        readonly List<(Transform beam, Target to, float ms)> beams = new List<(Transform, Target, float)>();
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
            c.Setup();
            return c;
        }

        void Add(ItemData it)
        {
            var type = Shop.DeviceType(it);
            var e = new Entry
            {
                item = it,
                name = GameNames.Item(it.index),
                rules = new Device(it.index, type, it.Attr(Device.DurationAttr), it.Attr(Device.CooldownAttr, Device.DefaultCooldownMs),
                                   type == Device.Type.StrikeCharge ? it.Attr(Device.ChargeAttr) : 0),
                radius = it.Attr(Device.RadiusAttr),
                growMs = it.Attr(Device.GrowAttr),
                slowPercent = it.Attr(Device.SlowAttr, 25),
                shieldCost = it.Attr(Device.ShieldCostAttr),
                damagePercent = it.Attr(Device.DamagePercentAttr, 100),
                coneDeg = it.Attr(Device.ConeAttr, 30),
                damage = it.Attr(9),
                emp = it.Attr(10),
            };
            devices.Add(e);
        }

        void Setup()
        {
            assets = CombatAssets.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            if (weapons != null) weapons.PrimaryDamageDealt += OnDamageDealt;
            SetupEffects();
        }

        void OnDestroy()
        {
            if (weapons != null) { weapons.PrimaryDamageDealt -= OnDamageDealt; weapons.DamageBoost = 1f; }
            if (self != null && (object)self.Deflects == (object)deflect) self.Deflects = null;
            DestroyEffects();
        }

        void OnDamageDealt(float damage)
        {
            foreach (var e in devices) e.rules.AddCharge(damage);
        }

        /// <summary>The quick menu's entry / the key for device 'slot' (mount order).</summary>
        public void Use(int slot)
        {
            if (slot < 0 || slot >= devices.Count || self == null || !self.Alive) return;
            if (Time.timeScale <= 0f || (Blocked != null && Blocked())) return;
            var e = devices[slot];
            if (e.rules.State == Device.Phase.Ready && !e.rules.Charged)
            {
                Message?.Invoke(string.Format(Localization.Extra("deviceNoCharge", "{0}: not charged yet ({1} %)"), e.name,
                                              Mathf.FloorToInt(e.rules.RechargeRate * 100f)));
                return;
            }
            if (!e.rules.Available) return;
            if (!CanFire(e)) return;
            if (!e.rules.TryFire()) return;
            Fire(e);
        }

        /// <summary>A death, a cutscene or a jump: every running device stops and cools down.</summary>
        public void Cancel()
        {
            foreach (var e in devices) if (e.rules.Cancel()) End(e);
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;
            if (dtMs <= 0f) return;   // paused
            for (int i = 0; i < Keys.Length && i < devices.Count; i++)
            {
                var key = Keys[i]();
                if (key != null && key.WasPressedThisFrame()) Use(i);
            }
            foreach (var e in devices)
            {
                if (e.rules.Update(dtMs, Cheats.NoEquipmentCooldown) == Device.Event.Ended) End(e);   // remake debug: no cooldown
                bool available = e.rules.Available;
                if (available && !e.wasAvailable) Message?.Invoke(string.Format(Localization.Extra("deviceReady", "{0} ready"), e.name));
                e.wasAvailable = available;
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
