// PlayerHull.cs
// Remake-only debug toy (the Debug page's Ships tab, CheatsCatalog.Hulls): the player flies any ship of the game, where they
// are. Every hull the original builds a ship from (Globals::getShipGroup: ships 0-63 with a model; 50 and 53 have none, the
// Vossk freighter 13 and the race freighters 15) and the capital ships it only ever places as NPC objects: the Terran
// battleship (ship 14, TrafficPlan: 30 % of the Terran orbits with freighters), the Terran carrier and the Vossk battleship
// (Level::createStaticObject 0x4974 / 0x4a6b), the Valkyrie battlestation (level 80) and the Void's mother ship
// (station_void). The capital ships keep their turrets (TrafficPlan.BattleshipTurrets / CarrierTurrets / VosskTurrets,
// ValkyrieLevels.StationTurrets with its shield generators as scenery), and ships 45 / 51 their fighter turret
// (Traffic.AttachFighterTurret), riding on the hull as the player's own auto turrets: they shoot whatever is hostile to the
// player and never hit the player (NpcShip.MakePlayerTurret).
// The hull is Session.ShipIndex (its ships.json stats, weapon mounts, save) plus, for the hulls that aren't that ship's own
// model (the race freighters and the objects outside ships.json, which fly with the battleship's stats: 100 hull, handling
// 50), the picked hull's key in PlayerPrefs; a ship bought or swapped another way drops it. Hulls the player can't normally
// own (the freighters 13 / 15, the battleship 14 and the capital ships; every other ship with a model is sold by some dealer
// (Shop.GenerateShips), lent by the story or sold by the Kaamo mechanics) never land in a hangar: the player's docking is
// refused and the story's moves into a station put the player back in their own ship (SpaceLevel.Dock, StationLevel).
// Hulls over 150 m across get a chase camera fitted to their size and are shrunk on the hangar's turntable. Nothing here is
// in the original.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PlayerHull
    {
        const float M = 0.05f;
        /// <summary>The battleship's stats for the capital ships outside ships.json.</summary>
        const int CapitalStats = 14;
        /// <summary>Radius (m) above which a hull is "big": fighters are under 90 m, the freighters from 300 m.</summary>
        const float BigRadius = 150f;
        const string HullPref = "cheat_hull", PreviousPref = "cheat_previousShip";

        public sealed class Hull
        {
            public string key;            // PlayerPrefs value: "ship_<n>" or the assembly name
            public string assembly;       // assemblies.json group
            public int stats;             // Session.ShipIndex while it flies (ships.json stats, mounts)
            public string label;
            public string category;       // the Ships tab's "Ship type": the race, Other, or Not normally flyable
            public bool own;              // the ship's own model (Database.ShipAssembly): the player engine glow and exhaust
            public bool playerShip;       // a ship the player can normally own: it may land in a hangar
            public (Vector3 pos, Vector3 rot)[] turrets;   // game offsets / rotations from the host, as the NPC places them
            public string turretAssembly;
            public float turretScale = 1f;
            public int turretGun = -1;    // a gun item for the turrets (the fighter turret's 22), -1 = the turret's own
            public float turretDamage = 1f;
            public (Vector3 pos, Vector3 rot)[] scenery;   // the Valkyrie's shield generators (no gun)
            public string sceneryAssembly;
            public bool holdAfterOneOff;  // the Void ship: its real size is the pose after its animation's first key
        }

        static List<Hull> hulls;
        static Database hullsDb;
        static string pickedKey;   // PlayerPrefs HullPref, read once (the docking check asks every frame)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            hulls = null;
            hullsDb = null;
            pickedKey = null;
            bigCache.Clear();
        }

        /// <summary>The tables the list was built from (Database.Load reads every file: once).</summary>
        static Database Db => hullsDb ??= Database.Load();

        static string PickedKey => pickedKey ??= PlayerPrefs.GetString(HullPref, "");

        static void SetPicked(string key)
        {
            pickedKey = key ?? "";
            if (pickedKey.Length > 0) PlayerPrefs.SetString(HullPref, pickedKey); else PlayerPrefs.DeleteKey(HullPref);
            PlayerPrefs.Save();
        }

        static string X(string key, string english) => Localization.Extra(key, english);

        static string RaceName(int race)
        {
            string n = Localization.Get(406 + race);
            return string.IsNullOrEmpty(n) ? "#" + race : n;
        }

        /// <summary>Every hull the player can fly: the ships by index, then the capital ships.</summary>
        public static List<Hull> All(Database db)
        {
            if (hulls != null) return hulls;   // the same for every economy: built once
            hullsDb ??= db;
            hulls = new List<Hull>();
            foreach (var ship in db.Ships)
            {
                int i = ship.index;
                if (i == 15)
                {
                    // Globals::getShipGroup idx 15: the freighter by race (the Vossk one is ship 13).
                    foreach (int race in new[] { 0, 2, 3 })
                        hulls.Add(new Hull
                        {
                            key = NpcTables.FreighterAssembly(race), assembly = NpcTables.FreighterAssembly(race), stats = 15,
                            label = $"15 · {string.Format(X("debugHullFreighter", "{0} freighter"), RaceName(race))}",
                        });
                    continue;
                }
                var a = db.ShipAssembly(i);
                if (a == null) continue;   // 50, 53: no model
                string name = i == 13 ? string.Format(X("debugHullFreighter", "{0} freighter"), RaceName(1))
                            : i == 14 ? X("cheatBattleship", "Terran battleship")
                            : CustomShips.ShipName(i);
                if (string.IsNullOrEmpty(name) || name == "-") name = ship.name;
                // Every ship with a model is the player's somewhere (dealers, the story's loaners, Kaamo) but the freighter 13 and
                // the battleship 14 (and 15 above).
                var h = new Hull { key = "ship_" + i, assembly = a.name, stats = i, label = $"{i} · {name}", own = i != 13, playerShip = i != 13 && i != 14 };
                if (i == 14) { h.turrets = TrafficPlan.BattleshipTurrets; h.turretAssembly = "turret_002_static"; h.turretScale = 6f; }
                // Level::createFighterTurrets: ships 45 / 51 carry turret_002_static, gun 22 x0.5 (ship-relative (-x, y, z) =
                // these host offsets).
                if (i == 45 || i == 51)
                {
                    h.turrets = new[] { (i == 45 ? new Vector3(0f, 172.25f, -460.7f) : new Vector3(0f, 470f, -83f), Vector3.zero) };
                    h.turretAssembly = "turret_002_static";
                    h.turretGun = 22;
                    h.turretDamage = 0.5f;
                }
                hulls.Add(h);
            }
            hulls.Add(new Hull
            {
                key = "sn_carrier_terran_1", assembly = "sn_carrier_terran_1", stats = CapitalStats,
                label = X("debugHullCarrier", "Terran carrier"),
                turrets = TrafficPlan.CarrierTurrets, turretAssembly = "turret_002_static", turretScale = 6f,
            });
            var vossk = new List<(Vector3, Vector3)>();
            foreach (var p in TrafficPlan.VosskTurrets) vossk.Add((p, Vector3.zero));
            hulls.Add(new Hull
            {
                key = "sn_battleship_vossk", assembly = "sn_battleship_vossk", stats = CapitalStats,
                label = X("debugHullVosskBattleship", "Vossk battleship"),
                turrets = vossk.ToArray(), turretAssembly = "turret_003_static", turretScale = 6f,
            });
            // Level 80 (ValkyrieLevels.Build80): the turrets at host + (-x, y, -z), rotation (0, 0, -rz).
            var guns = new List<(Vector3, Vector3)>();
            var shields = new List<(Vector3, Vector3)>();
            foreach (var t in ValkyrieLevels.StationTurrets)
                (t.shield ? shields : guns).Add((new Vector3(-t.pos.x, t.pos.y, -t.pos.z), new Vector3(0f, 0f, -t.rz)));
            hulls.Add(new Hull
            {
                key = "v_station_battlestation_anim_mission_object", assembly = "v_station_battlestation_anim_mission_object", stats = CapitalStats,
                label = X("debugHullValkyrie", "Valkyrie (battlestation)"),
                turrets = guns.ToArray(), turretAssembly = "v_station_battlestation_turret",
                scenery = shields.ToArray(), sceneryAssembly = "v_station_battlestation_shield",
            });
            hulls.Add(new Hull
            {
                key = "station_void", assembly = "station_void", stats = CapitalStats,
                label = X("debugHullVoid", "Void mother ship"),
                holdAfterOneOff = true,   // OrbitBuilder.SpawnStation: scale 1 at t 0, its hull x10.065 (about 10 km) from 50 ms
            });
            hulls.RemoveAll(h => db.AssemblyByName(h.assembly) == null);
            foreach (var h in hulls) h.category = CategoryOf(h);
            return hulls;
        }

        /// <summary>The Ships tab's types in order: the races, Other, then the hulls the player can't normally own.</summary>
        public static List<string> Categories(Database db)
        {
            var all = All(db);
            var order = new List<string>();
            foreach (int race in new[] { 0, 1, 2, 3, Standing.Pirate, Standing.Void }) order.Add(RaceName(race));
            order.Add(OtherCategory);
            order.Add(NotFlyableCategory);
            order.RemoveAll(c => !all.Exists(h => h.category == c));
            return order;
        }

        /// <summary>The hulls of one Ships tab type, by ship number (the capital ships after the numbered ones).</summary>
        public static List<Hull> OfCategory(Database db, string category) => All(db).FindAll(h => h.category == category);

        static string OtherCategory => X("debugHullOther", "Other");
        static string NotFlyableCategory => X("debugHullNotFlyable", "Not normally flyable");

        /// <summary>A hull's type: not normally flyable (the freighters, the battleship, the capital ships), else the race in
        /// its model's name (ship_001_terran, sn_ship_044_elite_nivelian, ship_063_vossk_prototype...), else Other (the deep
        /// science, retro, Most Wanted, modified and prototype ships).</summary>
        static string CategoryOf(Hull h)
        {
            if (!h.playerShip) return NotFlyableCategory;
            string n = h.assembly;
            if (n.Contains("terran")) return RaceName(0);
            if (n.Contains("vossk")) return RaceName(1);
            if (n.Contains("nivelian")) return RaceName(2);
            if (n.Contains("midorian")) return RaceName(3);
            if (n.Contains("pirate")) return RaceName(Standing.Pirate);
            if (n.Contains("void")) return RaceName(Standing.Void);
            return OtherCategory;
        }

        /// <summary>The hull flown now: the picked one while Session.ShipIndex is still its ship, else the ship's own.</summary>
        public static Hull Current(Database db)
        {
            var all = All(db);
            string key = PickedKey;
            var picked = key.Length > 0 ? all.Find(h => h.key == key) : null;
            if (picked != null && picked.stats == Session.ShipIndex) return picked;
            // The ship's own model; ships 13 / 15 have none of their own (the freighters): the first freighter.
            return all.Find(h => h.own && h.stats == Session.ShipIndex) ?? all.Find(h => h.stats == Session.ShipIndex);
        }

        /// <summary>The hull flown is one the player can normally own (any but the freighters, the battleship and the capital
        /// ships): only those land in a hangar.</summary>
        public static bool PlayerShip => Current(Db)?.playerShip ?? true;

        /// <summary>Back in the ship flown before the first debug hull, without rebuilding anything (a scene change follows):
        /// the story moving the player into a station, a station loaded with a hull no hangar takes.</summary>
        public static void ForceOwnShip()
        {
            int ship = PlayerPrefs.GetInt(PreviousPref, 10);
            var own = All(Db).Find(h => h.stats == ship);
            if (own == null || !own.playerShip) ship = 10;   // the Phantom, a new game's ship
            PlayerPrefs.DeleteKey(PreviousPref);
            SetPicked(null);
            Session.ShipIndex = ship;
        }

        /// <summary>The model for the player's ship 'shipIndex' (the picked hull while it is that ship).</summary>
        public static AssemblyData Assembly(Database db, int shipIndex)
        {
            if (shipIndex == Session.ShipIndex)
            {
                var h = Current(db);
                if (h != null) return db.AssemblyByName(h.assembly);
            }
            return db.ShipAssembly(shipIndex);
        }

        /// <summary>The player's model just made: posed like the original shows that hull (the Void ship held after its
        /// one-off first key, its full size, like OrbitBuilder.SpawnStation in the alien orbit). Before FitCamera.</summary>
        public static void PrepareModel(Database db, GameObject model)
        {
            if (model == null) return;
            var h = Current(db);
            if (h != null && h.holdAfterOneOff) PartAnimation.HoldAllAfterOneOff(model);
            else if (h != null && h.assembly == "v_station_battlestation_anim_mission_object") PartAnimation.HoldAllAtEnd(model);   // unfolded, like levels 80 / 154
        }

        /// <summary>The player's engine glow and exhaust particles belong to the ship's own model (the freighters and the
        /// capital ships have none of their own).</summary>
        public static bool OwnEngines(Database db) => Current(db)?.own ?? true;

        static readonly Dictionary<string, bool> bigCache = new Dictionary<string, bool>();

        /// <summary>The hull flown is over 150 m across: fitted camera, no docking, shrunk in the hangar.</summary>
        public static bool Big
        {
            get
            {
                var db = Db;
                var h = Current(db);
                if (h == null) return false;
                if (bigCache.TryGetValue(h.key, out bool big)) return big;
                var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(h.assembly));
                big = prefab != null && LocalBounds(prefab.transform, prefab.transform).extents.magnitude > BigRadius;
                bigCache[h.key] = big;
                return big;
            }
        }

        /// <summary>The flown hull's name (refusals).</summary>
        public static string Label => NameOf(Current(Db));

        /// <summary>The hull's name without the picker's ship number ("14 · Terran battleship" -> "Terran battleship").</summary>
        static string NameOf(Hull h)
        {
            if (h == null) return "";
            int i = h.label.IndexOf(" · ");
            return i >= 0 ? h.label.Substring(i + 3) : h.label;
        }

        /// <summary>The model's size in world metres (the hull, not its glows).</summary>
        static float Radius(Transform model) =>
            model == null ? 0f : LocalBounds(model, model).extents.magnitude * Mathf.Abs(model.lossyScale.x);

        /// <summary>The model's bounds in 'root's space, from every mesh's own bounds (works on prefab assets too). The
        /// additive layers (glows, halos: the Void ship's glow sheet is 15 km across, its hull 1.6 km) don't count, unless
        /// the model has nothing else.</summary>
        static Bounds LocalBounds(Transform root, Transform model)
        {
            var b = LocalBounds(root, model, false);
            return b.size == Vector3.zero ? LocalBounds(root, model, true) : b;
        }

        static Bounds LocalBounds(Transform root, Transform model, bool withAdditive)
        {
            bool any = false;
            var b = new Bounds();
            var toRoot = root.worldToLocalMatrix;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || (!withAdditive && mf.name.Contains("_add"))) continue;
                var lb = mf.sharedMesh.bounds;
                var m = toRoot * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>The player's model and chase camera for the hull flown: a big hull's model is shifted so the ship turns
        /// around its middle (the battleship's pivot is at its bow), the camera sits just behind the stern and above the
        /// deck, and the far plane reaches past the bow; any other ship gets the original's offsets (TargetFollowCamera:
        /// (0, 600, -1338) / (0, 600, -650) units) and the level's far plane.</summary>
        public static void FitCamera(Transform root, Transform model, ChaseCamera chase, float levelFarClip)
        {
            var cam = chase != null ? chase.GetComponent<Camera>() : null;
            if (!Big || model == null)
            {
                if (chase != null) { chase.offset = new Vector3(0f, 600f, -1338f) * M; chase.lookOffset = new Vector3(0f, 600f, -650f) * M; }
                if (cam != null) cam.farClipPlane = levelFarClip;
                return;
            }
            var b = LocalBounds(root, model);
            model.localPosition -= b.center;   // the hull's middle on the pivot
            if (chase == null) return;
            float halfLength = b.extents.z, halfHeight = b.extents.y;
            chase.offset = new Vector3(0f, halfHeight * 1.6f, -halfLength * 1.25f);
            chase.lookOffset = new Vector3(0f, halfHeight * 1.1f, halfLength * 0.1f);
            if (cam != null) cam.farClipPlane = Mathf.Max(levelFarClip, chase.offset.magnitude + b.extents.magnitude * 2f + 2000f);
        }

        /// <summary>The hangar's turntable: a big hull shrunk to a large ship's size so the hangar camera sees it.</summary>
        public static void FitHangar(Transform ship)
        {
            if (!Big) return;
            float r = Radius(ship);
            if (r > 60f) ship.localScale *= 60f / r;
        }

        /// <summary>The player's hull turrets and scenery gone (another hull): they vanish without an explosion.</summary>
        public static void RemoveTurrets(SpaceLevel level)
        {
            if (level == null || level.Player == null) return;
            foreach (var t in level.Player.GetComponentsInChildren<NpcShip>(true))
            {
                if (!t.PlayerOwned) continue;
                t.Vanish();
                t.transform.SetParent(null, true);
                t.gameObject.SetActive(false);
            }
            foreach (var s in level.Player.GetComponentsInChildren<Transform>(true))
                if (s != null && s.name.StartsWith(SceneryPrefix)) Object.Destroy(s.gameObject);
        }

        const string SceneryPrefix = "Hull scenery ";

        /// <summary>The Debug page's Ships tab: fly 'hull' (the old hull gone, the player flying the new one at once); docked
        /// only a hull the hangar can take (the turntable shows it). The result text.</summary>
        public static string Fly(Hull hull, SpaceLevel level, StationLevel docked)
        {
            if (hull == null) return "";
            var db = Db;
            var before = Current(db);
            if (before == hull) return string.Format(X("debugHullSame", "You already fly the {0}."), NameOf(hull));
            // Docked: the hangar takes only the ships the player can normally own.
            if (level == null && !hull.playerShip)
                return string.Format(X("debugHullLaunchFirst", "The {0} doesn't fit in the hangar: launch first."), NameOf(hull));
            // The ship to go back to: the one flown before the first debug hull (a pick the player has since flown away from,
            // by buying or a story swap, counts as none).
            bool onDebugHull = PickedKey.Length > 0 && before != null && before.key == PickedKey;
            if (!onDebugHull) PlayerPrefs.SetInt(PreviousPref, Session.ShipIndex);
            SetPicked(hull.key);
            if (level != null) level.SwapPlayerShip(hull.stats);
            else
            {
                Session.ShipIndex = hull.stats;
                docked?.ReplacePlayerShip(hull.stats);
            }
            return string.Format(X("debugHullFlying", "You fly the {0}."), NameOf(hull));
        }

        /// <summary>Back to the ship flown before the first debug hull.</summary>
        public static string Restore(SpaceLevel level, StationLevel docked)
        {
            var current = Current(Db);
            if (PickedKey.Length == 0 || current == null || current.key != PickedKey) { SetPicked(null); return X("debugHullOwn", "You already fly your own ship."); }
            int ship = PlayerPrefs.GetInt(PreviousPref, 10);
            PlayerPrefs.DeleteKey(PreviousPref);
            SetPicked(null);
            if (level != null) level.SwapPlayerShip(ship);
            else
            {
                Session.ShipIndex = ship;
                docked?.ReplacePlayerShip(ship);
            }
            return X("cheatBattleshipOff", "Back in your own ship.");
        }

        /// <summary>The hull's turrets (and the Valkyrie's shield generators) on the player's model, after the level's
        /// traffic exists (they are traffic turrets).</summary>
        public static void AttachTurrets(SpaceLevel level)
        {
            if (level == null || level.Traffic == null || level.Player == null) return;
            var hull = Current(level.Database);
            if (hull == null || (hull.turrets == null && hull.scenery == null)) return;
            var root = level.Player.transform;
            var model = level.Player.visualModel;
            // The NPC host's root faces game +z (NpcShip.GameForward = OrbitLayout.RotationToUnity(0), Euler(0, 180, 0)) with the
            // model unturned inside; the player's model faces Unity +z: the offsets turn by the inverse of that. On the model,
            // not the root: ShipController banks and pitches the model when turning (and Mining / the death tumble turn it), so
            // the turrets ride with the hull. The offsets are from the model's own origin, in its unscaled space (the
            // battleship's prefab is x2).
            var toPlayer = Quaternion.Inverse(Quaternion.Euler(0f, 180f, 0f));
            void Place(Transform t, Vector3 pos, Vector3 rot)
            {
                var offset = toPlayer * (new Vector3(pos.x, pos.y, -pos.z) * M);
                var turn = toPlayer * OrbitLayout.RotationToUnity(rot);
                if (model != null)
                {
                    float scale = Mathf.Max(0.0001f, model.localScale.x);
                    var keep = t.localScale;
                    t.SetParent(model, false);
                    t.localPosition = offset / scale;
                    t.localRotation = turn;
                    t.localScale = keep / scale;
                }
                else
                {
                    t.SetParent(root, false);
                    t.localPosition = offset;
                    t.localRotation = turn;
                }
            }
            if (hull.turrets != null)
                foreach (var (pos, rot) in hull.turrets)
                {
                    var spec = new SpawnSpec
                    {
                        group = NpcGroup.Turret, race = 0, ship = -1, turretAssembly = hull.turretAssembly, scale = hull.turretScale,
                        hitpoints = 1000, noLoot = true, nameText = 1666, stationary = true, alwaysFriend = true, rotation = rot,
                        radarHidden = true, position = new Vector3(root.position.x, root.position.y, -root.position.z) / M,
                    };
                    var t = level.Traffic.SpawnShip(spec);
                    if (t == null) continue;
                    Place(t.transform, pos, rot);
                    t.Target.invulnerable = true;
                    t.Target.radius = 0f;
                    t.Target.untargetable = true;
                    if (hull.turretGun >= 0) t.SetGun(hull.turretGun, hull.turretDamage);
                    t.MakePlayerTurret();
                }
            if (hull.scenery != null)
            {
                var prefab = AssembledObject.LoadPrefab(level.Database.AssemblyByName(hull.sceneryAssembly));
                if (prefab != null)
                    foreach (var (pos, rot) in hull.scenery)
                    {
                        var go = Object.Instantiate(prefab);
                        go.name = SceneryPrefix + hull.sceneryAssembly;
                        foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
                        Place(go.transform, pos, rot);
                    }
            }
            level.Traffic.ConnectPlayers();   // the turrets' target lists (every other race's ship)
        }
    }
}
