// WeaponFx.cs
// Visuals and sound of one weapon item, from the original's per-item tables (weapons.md section 4:
// projectile DAT_00252d1c, muzzle flash DAT_00259ab8, impact DAT_00259220, shot sound DAT_00252310).
// One asset per item in Resources/GoF2Weapons/item_XXX (made by GoF2 > Build > Weapon Fx from
// Resources/GoF2Data/weapon_fx.json), loaded only for the equipped weapons.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class WeaponFx : ScriptableObject
    {
        public const string ResourcesFolder = "GoF2Weapons";

        public int item;
        public GameObject projectile;
        public GameObject muzzleFlash;
        public GameObject impact;
        public AudioClip shot;
        [Tooltip("Every wave of the shot event (random pick per shot); shot = the first.")]
        public AudioClip[] shots;
        [Tooltip("Auto-cannons, thermo guns and turrets loop their sound while firing (Player::playShootSound).")]
        public bool shotLoops;
        [Tooltip("Bombs, mines, scatter shells, the shock blast: Explosion type (0 ship, 7 EMP, 8-10 scatter, 11 shock blast, 13 fireworks), -1 none.")]
        public int explosionType = -1;
        [Tooltip("Explosion::playSound by weapon (weapons_special.md 3.4).")]
        public AudioClip explosionSound;
        public AudioClip[] explosionSounds;
        /// <summary>Remake mods (items.json fx "trail", ModWeapons): the smoke trail behind each shot; null = the game's.</summary>
        [System.NonSerialized] public TrailLook trail;
        /// <summary>Remake mods (a modder's wish, 2026-10-18): the mod gave "impact" itself (ModWeapons), so it also shows
        /// where a missile hits and where a bomb goes off, in place of the game's burst / explosion (GunRig.ShowBlast).</summary>
        [System.NonSerialized] public bool modImpact;
        [Tooltip("The Liberator's engine loop (1116) while it is steered.")]
        public AudioClip engineLoop;
        [Tooltip("1116's other layer (EngineDLC_06): plays while its Vertical parameter is 0.336..0.996.")]
        public AudioClip engineLoopExtra;
        [Tooltip("Turret items: the assembled *_ship_mounted prefab (pivot -> base + gun).")]
        public GameObject turretMounted;
        /// <summary>A mod turret's own model (items.json fx "turret", Modding.ModWeapons): turretMounted is it, in flight and
        /// in the hangar; its muzzle and where its shots start along the gun node (game units, -1 = the base item's).</summary>
        [System.NonSerialized] public bool modTurret;
        [System.NonSerialized] public float turretMuzzleUnits = -1f, turretShotUnits = -1f;
        [Tooltip("Sentry guns: the deployed object (Assembled/supernova/turrets/sn_sentry_gun_00X).")]
        public GameObject sentry;

        /// <summary>A shot sound: one of the event's waves at random (its sound definition's weighted random / shuffle).</summary>
        public AudioClip Shot => Pick(shots, shot);
        public AudioClip ExplosionSound => Pick(explosionSounds, explosionSound);

        static AudioClip Pick(AudioClip[] list, AudioClip fallback) =>
            list != null && list.Length > 0 ? list[Random.Range(0, list.Length)] : fallback;

        /// <summary>The item's fx: a mod item's own (items.json "fx", Modding.ModWeapons), else its base item's
        /// (Modding.ModContent.ItemLook).</summary>
        public static WeaponFx Load(int item) =>
            Modding.ModWeapons.Fx(item) ?? Resources.Load<WeaponFx>($"{ResourcesFolder}/item_{Modding.ModContent.ItemLook(item):000}");
    }

    /// <summary>Remake mods, items.json fx "trail" (ModWeapons): the smoke trail behind a weapon's shots, over the game's
    /// (RocketTrail.For) or added to a weapon without one. Null on WeaponFx = the game's own.</summary>
    [System.Serializable]
    public sealed class TrailLook
    {
        /// <summary>"trail": false: none at all.</summary>
        public bool none;
        /// <summary>Its colour at the start (it fades to nothing), white = the game's.</summary>
        public Color color = Color.white;
        /// <summary>Half its width in game units, its life in ms; -1 = the game record's (a rocket's 100 / 3000).</summary>
        public float width = -1f, lifeMs = -1f;
    }
}
