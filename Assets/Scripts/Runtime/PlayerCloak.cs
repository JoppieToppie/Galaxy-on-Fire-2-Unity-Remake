// PlayerCloak.cs
// Runs the player's cloak (Cloak rules, Reference/research/combat_equipment.md 1) on the player ship.
//   Control: the autopilot menu's cloak entry (the original's HUD flight menu, Hud::initHudMenu(0) button 0x800, label
//     the cloak item's name) plus a remake shortcut (C / right stick press). Not enough cells: 583 + attr 38.
//   Paying: "-N t Energy Cells" (1396); the charge bar "Cloak charging" (317) in the HUD; "Cloak ready" (316).
//   Cloaked: Target.cloaked (Player+0x5e): NPCs keep chasing but don't fire, turrets don't aim, sleepers don't wake;
//     medal 19 counts the time (Status+0xc0). Sound 30 (Cloak_02) on and off.
//   Look (BumpShaderCloak, shader type 0xe): the hull renderers switch to GoF2/Cloak (dissolve by cloak_map.png into the
//     refracted screen behind, light-blue edge); the lights keep glowing and animating, the engine glow hides from 25 %
//     (CloakGlow). Drawn by CloakPass from a copy of the frame after the transparents, so the hull refracts its own lights.
//   Remake (for mods): a cloak item with attribute 104 = 1 ("phaseCloak") also phases the ship out while cloaked, fades
//     included: PlayerCollision skips the landmarks, the freighters, other players' ships and the asteroids (the wormhole
//     still pulls), and Target.phased lets shots fly through and blasts pass (no damage at all; NetPlayer tells the other
//     players' games). Ending inside something collides as usual: pushed out of a volume, an asteroid destroyed for 20.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    public class PlayerCloak : MonoBehaviour
    {
        const int IntegratedCloakItem = 95;
        /// <summary>Remake-only item attribute (Modding.ItemStats "phaseCloak").</summary>
        public const int PhaseAttr = 104;
        static readonly int AnimValueId = Shader.PropertyToID("_AnimValue"), CloakRateId = Shader.PropertyToID("_CloakRate");

        public Cloak Rules { get; private set; }
        public string ItemName { get; private set; }
        /// <summary>Remake: the item phases the ship through objects while cloaked (attribute 104).</summary>
        public bool Phases { get; private set; }
        /// <summary>Phased out now: PlayerCollision lets the ship pass through everything solid.</summary>
        public bool Phasing => Phases && Rules != null && Rules.Cloaked;
        /// <summary>A HUD message ("-2t Energy Cells", "Cloak ready", 583).</summary>
        public event Action<string> Message;

        Target target;
        CombatAssets assets;
        AudioSource sfx;
        static InputAction action => GameControls.Cloak;   // rebindable (C / right stick press)
        readonly List<(Renderer r, Material[] original, Material[] cloak)> hull = new List<(Renderer, Material[], Material[])>();
        readonly CloakGlow glow = new CloakGlow { KeepEngine = true };   // #85: the engine glow stays
        bool swapped;

        public static bool HasCloak(Database db, int ship) =>
            ship == 44 || ship == 49 || Shop.FirstMounted(db, 21) != null;

        /// <summary>PlayerEgo::PlayerEgo (cloak part): null when the ship has no cloak.</summary>
        public static PlayerCloak Attach(GameObject player, Database db, int ship, Target target, Transform model)
        {
            var item = Shop.FirstMounted(db, 21) ?? (ship == 44 || ship == 49 ? db.Item(IntegratedCloakItem) : null);
            if (item == null) return null;
            var c = player.AddComponent<PlayerCloak>();
            c.Rules = new Cloak(item.index, item.Attr(35, 10000), item.Attr(36, 2000), item.Attr(38, 1), Session.Difficulty);
            c.ItemName = GameNames.Item(item.index);
            c.Phases = item.Attr(PhaseAttr) > 0;
            c.target = target;
            c.Setup(model);
            return c;
        }

        void Setup(Transform model)
        {
            assets = CombatAssets.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            if (model == null) return;
            var shader = assets != null ? assets.cloakShader : null;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool lit = mats.Length > 0 && mats[0] != null && mats[0].shader != null && mats[0].shader.name.Contains("Lit");
                if (!lit) { glow.Add(r, model); continue; }
                if (shader == null) continue;
                var cloak = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = new Material(shader);
                    if (mats[i] != null)
                    {
                        if (mats[i].HasProperty("_BaseMap")) m.SetTexture("_BaseMap", mats[i].GetTexture("_BaseMap"));
                        if (mats[i].HasProperty("_BumpMap")) m.SetTexture("_BumpMap", mats[i].GetTexture("_BumpMap"));
                    }
                    if (assets.cloakMap != null) m.SetTexture("_CloakMap", assets.cloakMap);
                    cloak[i] = m;
                }
                hull.Add((r, mats, cloak));
            }
        }

        void OnDestroy()
        {
            if (target != null) { target.cloaked = false; target.phased = false; }
            GoF2Remake.Visuals.CloakPass.Request(this, false);
            foreach (var h in hull) foreach (var m in h.cloak) Destroy(m);
        }

        /// <summary>MGame::useCloak -> PlayerEgo::toggleCloaking.</summary>
        public void Use()
        {
            if (Rules == null || !Rules.Available || !target.Alive) return;
            bool free = Cheats.FreeJumps;   // remake: the Debug panel's free jumps cover the cloak's cells too
            if (!Rules.TryStart(free ? int.MaxValue : Shop.CargoOf(Cloak.EnergyCellItem)))
            {
                Message?.Invoke($"{Localization.Get(583)} {Rules.cells}.");
                return;
            }
            if (free) return;
            Shop.RemoveFromCargo(Cloak.EnergyCellItem, Rules.cells);
            Message?.Invoke($"-{Rules.cells}t {GameNames.Item(Cloak.EnergyCellItem)}");
        }

        void Update()
        {
            if (Rules == null) return;
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;
            if (dtMs <= 0f) return;   // paused
            if (action.WasPressedThisFrame()) Use();
            switch (Rules.Update(dtMs))
            {
                case Cloak.Event.Engaged:
                    target.cloaked = true;
                    target.phased = Phases;
                    Play();
                    Swap(true);
                    break;
                case Cloak.Event.Ended:
                    target.cloaked = false;
                    target.phased = false;
                    Play();
                    Swap(false);
                    break;
                case Cloak.Event.Ready:
                    Message?.Invoke(Localization.Get(316));
                    break;
            }
            if (!Rules.Cloaked) return;
            Session.CloakMs += (long)dtMs;
            float pct = Rules.Percentage;
            foreach (var h in hull)
                foreach (var m in h.cloak) { m.SetFloat(AnimValueId, pct / 100f); m.SetFloat(CloakRateId, Rules.Timer * 0.001f); }
            glow.Apply(pct);
        }

        void Swap(bool on)
        {
            if (swapped == on) return;
            swapped = on;
            foreach (var h in hull) if (h.r != null) h.r.sharedMaterials = on ? h.cloak : h.original;
            if (!on) glow.Restore();
            GoF2Remake.Visuals.CloakPass.Request(this, on);
        }

        void Play()
        {
            if (assets != null && assets.cloak != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(assets.cloak), Settings.SfxVolume);
        }
    }
}
