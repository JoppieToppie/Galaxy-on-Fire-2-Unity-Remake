// ModBuilderSpaceViewFx.cs
// The Mod Builder's Weapon fx kind (part of ModBuilderSpaceView), edited in space on a firing range: the project's weapons'
// "fx" (items.json, Modding/README.md "Weapon effects"). Left: the project's weapons (new items and changes made in the Items
// kind) and "Change a game weapon's look". Right: the projectile, the muzzle flash and the impact, each the base item's,
// another weapon's (its size), the base's recoloured, a sprite of the project's (a PNG: size, stretch along the flight, colour,
// glow, additive or not, lifetime, growth, fade, spin), a model (a .glb or the game's: size, colour, glow), or none; the shot
// and blast sounds (the base's, another weapon's or the project's own files), the shot's looping. The range fires the weapon
// at a target drone in front of the player's parked ship (Gun + GunRig, as other players' shots are drawn: NetShotMirror),
// again and again; every edit lets the game read the project and the range picks the new look up (ModWeapons).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderSpaceView
    {
        VisualElement fxList, fxPage;
        ProjectItem currentFx;

        static readonly string[] PartNames = { "Projectile", "Muzzle flash", "Impact" };
        static readonly string[] PartModes = { "Base's", "Game part", "Sprite", "Model", "None" };

        void BuildFxPanels(VisualElement left, VisualElement right)
        {
            fxList = ListPanel(left, T("mbFx", "Weapon fx"));
            Act(left, T("mbChangeGameFx", "Change a game weapon's look"), PickGameWeapon, "mbv-add", "mbv-change");
            fxPage = PagePanel(right);
        }

        static bool IsWeapon(int item) => Database.Shared.Item(item) is ItemData d && d.TypeId >= 0 && d.TypeId <= 2;

        int FxItemIndex(ProjectItem it) => it == null ? -1 : it.IsOverride ? it.OverrideIndex : ModContent.ItemIndexOf(Project.Key(it.LocalId));

        void RebuildFx()
        {
            fxList.Clear();
            if (Project == null) return;
            var weapons = Project.Items.Where(i => i.Original >= 0 && IsWeapon(i.Original)).ToList();
            if (currentFx != null && !weapons.Contains(currentFx)) currentFx = null;
            currentFx ??= weapons.FirstOrDefault();
            foreach (var w in weapons)
            {
                var it = w;
                int n = FxItemIndex(it);
                ListRow(fxList, !string.IsNullOrEmpty(it.Name) ? it.Name : GameNames.Item(it.Original),
                        it.IsOverride ? string.Format(T("mbChanges", "Changes {0}"), GameNames.Item(it.Original)) : string.Format(T("mbBasedOn", "New · based on {0}"), GameNames.Item(it.Original)),
                        it == currentFx, () => { currentFx = it; RebuildFx(); BuildFxPage(); RestartRange(); }, n >= 0 ? ItemInfo.ItemIcon(n) : ItemInfo.ItemIcon(it.Original));
            }
            if (weapons.Count == 0)
                Note(fxList, T("mbNoFxItems", "No weapons yet: make one in Items, or change a game weapon's look here."));
        }

        void PickGameWeapon()
        {
            var choices = new List<(string, Texture2D, Action)>();
            for (int i = 0; i < GameNames.OriginalItems; i++)
            {
                if (!IsWeapon(i) || ModContent.IsMissingItem(i) || Project.Items.Any(x => x.IsOverride && x.OverrideIndex == i)) continue;
                int index = i;
                choices.Add((GameNames.Item(i), ItemInfo.ItemIcon(i), () =>
                {
                    currentFx = Project.AddItemOverride(index);
                    MarkDirty();
                    RequestReload(false);
                    RebuildFx();
                    BuildFxPage();
                }));
            }
            ShowPicker(T("mbChangeGameFx", "Change a game weapon's look"), choices, true);
        }

        // ---- the fx object (live in the entry) ------------------------------------------------------------------

        JObject Fx(ProjectItem it, bool make)
        {
            if (it.Get("fx") is JObject fx) return fx;
            if (!make) return null;
            fx = new JObject();
            it.Set("fx", fx);
            return fx;
        }

        void DropEmptyFx(ProjectItem it) { if (Fx(it, false) is JObject fx && !fx.HasValues) it.Set("fx", null); }

        /// <summary>A part's mode: 0 the base's, 1 a game part (another item's / recoloured), 2 a sprite, 3 a model, 4 none.</summary>
        int PartMode(JToken p) => p == null ? 0 : p.Type == JTokenType.Boolean && !(bool)p ? 4 : p is JObject o ? (o["sprite"] != null ? 2 : o["model"] != null ? 3 : 1) : 0;

        void FxEdited(bool page = true)
        {
            MarkDirty();
            RequestReload(true);
            if (page) BuildFxPage();
        }

        void BuildFxPage()
        {
            fxPage.Clear();
            var it = currentFx;
            if (it == null) { Note(fxPage, T("mbPickFx", "Pick a weapon on the left; the range fires it again and again.")); return; }
            var original = Database.Shared.Item(it.Original);
            Heading(fxPage, !string.IsNullOrEmpty(it.Name) ? it.Name : GameNames.Item(it.Original));
            if (original != null) Note(fxPage, $"{ItemInfo.Category(original)}  ·  {string.Format(T("mbBasedOnShort", "based on {0}"), GameNames.Item(it.Original))}");
            var range = Row(fxPage);
            Act(range, rangePaused ? T("mbFireOn", "Fire") : T("mbFirePause", "Pause"), () => { rangePaused = !rangePaused; BuildFxPage(); }, "mbv-step");
            Act(range, T("mbFireAgain", "Reload the look"), RestartRange, "mbv-step");

            for (int k = 0; k < 3; k++)
            {
                string part = ProjectItem.FxParts[k];
                BuildPart(it, part, T("mbPart_" + part, PartNames[k]));
            }

            BuildTrail(it);

            // ---- sounds ----
            Heading(fxPage, T("mbSounds", "Sounds"));
            foreach (var (key, english) in new[] { ("shot", "Shot"), ("explosionSound", "Blast (bombs, mines)") })
            {
                string k = key;
                var fx = Fx(it, false);
                var v = fx?[k];
                string now = v == null ? T("mbBaseItems", "the base item's") : v.Type == JTokenType.Integer ? string.Format(T("mbModelOfItem", "{0}'s"), GameNames.Item((int)v))
                           : v.Type == JTokenType.String ? System.IO.Path.GetFileName((string)v) : v is JArray a ? string.Format(T("mbFilesN", "{0} files"), a.Count) : "?";
                var row = Row(fxPage);
                Add(row, new Label($"{T("mbSound_" + k, english)}: {now}"), "mbv-inline-label");
                Act(row, T("mbGameItemButton", "Game item…"), () => ShowPicker(T("mbSoundFrom", "The sound of"), WeaponChoices(i => { Fx(it, true)[k] = i; FxEdited(); })), "mbv-step");
                Act(row, T("mbSoundFile", "Sound file…"), () => PickAudio(T("mbPickSound", "Choose the sound file"), path =>
                {
                    string id = (it.LocalId ?? "item_" + it.OverrideIndex) + "_" + k;
                    string file = Project.AddFile(path, "fx", id, System.IO.Path.GetExtension(path).ToLowerInvariant());
                    var fxo = Fx(it, true);
                    if (fxo[k] is JArray list) list.Add(file);
                    else fxo[k] = file;
                    FxEdited();
                }), "mbv-step");
                if (v is JValue sv && sv.Type == JTokenType.String) Act(row, T("mbAnother", "+ another"), () => PickAudio(T("mbPickSound", "Choose the sound file"), path =>
                {
                    string id = (it.LocalId ?? "item_" + it.OverrideIndex) + "_" + k;
                    string file = Project.AddFile(path, "fx", id, System.IO.Path.GetExtension(path).ToLowerInvariant());
                    Fx(it, true)[k] = new JArray((string)sv, file);
                    FxEdited();
                }), "mbv-step");
                if (v != null) Act(row, "×", () => { Fx(it, true).Remove(k); DropEmptyFx(it); FxEdited(); }, "mbv-step");
            }
            var fxNow = Fx(it, false);
            int loops = fxNow?["shotLoops"] is JValue lv && lv.Type == JTokenType.Boolean ? ((bool)lv ? 1 : 2) : 0;
            Segments(fxPage, T("mbShotLoops", "Shot sound while firing"), new[] { T("mbAsBase", "As the base"), T("mbLoops", "Loops"), T("mbPerShot", "Once a shot") }, loops, n =>
            {
                if (n == 0) { Fx(it, true).Remove("shotLoops"); DropEmptyFx(it); } else Fx(it, true)["shotLoops"] = n == 1;
                FxEdited();
            });
            Note(fxPage, T("mbFxNote", "The weapon still works like its base item; every gun of the item uses this look."));
        }

        List<(string, Texture2D, Action)> WeaponChoices(Action<int> pick)
        {
            var l = new List<(string, Texture2D, Action)>();
            for (int i = 0; i < GameNames.OriginalItems; i++)
            {
                if (!IsWeapon(i) || ModContent.IsMissingItem(i)) continue;
                int index = i;
                l.Add((GameNames.Item(i), ItemInfo.ItemIcon(i), () => pick(index)));
            }
            return l;
        }

        void BuildPart(ProjectItem it, string part, string label)
        {
            Heading(fxPage, label);
            var fx = Fx(it, false);
            var p = fx?[part];
            int mode = PartMode(p);
            Segments(fxPage, null, PartModes.Select(m => T("mbFxMode_" + m.Replace(" ", "").Replace("'", ""), m)).ToArray(), mode, n =>
            {
                if (n == mode) return;
                var f = Fx(it, true);
                switch (n)
                {
                    case 0: f.Remove(part); break;
                    case 1: f[part] = new JObject { ["item"] = it.Original }; break;
                    case 2:
                        PickImage(T("mbPickSprite", "Choose the sprite (white on black)"), path =>
                        {
                            string file = Project.AddFile(path, "fx", (it.LocalId ?? "item_" + it.OverrideIndex) + "_" + part, ".png");
                            Fx(it, true)[part] = new JObject { ["sprite"] = file, ["size"] = part == "impact" ? 2400 : part == "muzzle" ? 500 : 450 };
                            FxEdited();
                        });
                        return;
                    case 3: f[part] = new JObject { ["model"] = "gof2:rocket_002", ["size"] = 300 }; break;
                    case 4: f[part] = false; break;
                }
                DropEmptyFx(it);
                FxEdited();
            });
            if (!(p is JObject o)) return;
            void Set(string k, JToken v) { if (v == null) o.Remove(k); else o[k] = v; FxEdited(false); }
            float Num(string k, float d) => o[k] is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : d;
            bool Flag(string k, bool d) => o[k] is JValue v && v.Type == JTokenType.Boolean ? (bool)v : d;
            if (mode == 1)
            {
                var row = Row(fxPage);
                int from = o["item"] is JValue iv && iv.Type == JTokenType.Integer ? (int)iv : it.Original;
                Add(row, new Label(string.Format(T("mbModelOfItem", "{0}'s"), GameNames.Item(from))), "mbv-inline-label");
                Act(row, T("mbGameItemButton", "Game item…"), () => ShowPicker(string.Format(T("mbPickPartFrom", "{0} from"), label), WeaponChoices(i => { o["item"] = i; FxEdited(); })), "mbv-step");
                Int(fxPage, T("mbScalePercent", "Size (%)"), Mathf.RoundToInt(Num("scale", 1f) * 100f), 1, 10000, v => Set("scale", Mathf.Approximately(v, 100) ? null : new JValue(v / 100f)));
            }
            if (mode == 2)
            {
                var row = Row(fxPage);
                Add(row, new Label(System.IO.Path.GetFileName((string)o["sprite"] ?? "")), "mbv-inline-label");
                Act(row, T("mbReplaceFile", "Replace…"), () => PickImage(T("mbPickSprite", "Choose the sprite (white on black)"), path =>
                {
                    o["sprite"] = Project.AddFile(path, "fx", (it.LocalId ?? "item_" + it.OverrideIndex) + "_" + part, ".png");
                    FxEdited();
                }), "mbv-step");
                Float(fxPage, T("mbSizeUnits", "Size (units; a blaster bolt is ~1600)"), Num("size", 200f), 1f, 100000f, v => Set("size", v));
                var frow = Row(fxPage);
                FlagFx(frow, T("mbAdditive", "Glowing (additive)"), Flag("additive", true), v => Set("additive", v ? null : new JValue(false)));
                if (part == "projectile")
                {
                    FlagFx(frow, T("mbAlongFlight", "Along its flight"), Flag("alongFlight", false), v => Set("alongFlight", v ? new JValue(true) : null));
                    Float(fxPage, T("mbStretch", "Length (x its size)"), Num("stretch", 1f), 0.1f, 100f, v => Set("stretch", Mathf.Approximately(v, 1f) ? null : new JValue(v)));
                }
                else
                {
                    Float(fxPage, T("mbLifetime", "Lasts (ms)"), Num("lifetime", part == "muzzle" ? 100f : 300f), 10f, 10000f, v => Set("lifetime", v));
                    Float(fxPage, T("mbGrow", "Grows to (x)"), Num("grow", 1f), 0.01f, 50f, v => Set("grow", Mathf.Approximately(v, 1f) ? null : new JValue(v)));
                    FlagFx(frow, T("mbFades", "Fades out"), Flag("fade", true), v => Set("fade", v ? null : new JValue(false)));
                }
                Float(fxPage, T("mbSpin", "Spin (°/s)"), Num("spin", 0f), -3600f, 3600f, v => Set("spin", Mathf.Approximately(v, 0f) ? null : new JValue(v)));
            }
            if (mode == 3)
            {
                var row = Row(fxPage);
                string model = (string)o["model"] ?? "";
                Add(row, new Label(GameModels.Is(model, out string gm) ? gm : System.IO.Path.GetFileName(model)), "mbv-inline-label");
                Act(row, T("mbGameModel", "Game model…"), () =>
                {
                    var names = Database.Shared.Assemblies.Where(a => a.pack != ModContent.Pack && a.name != null
                                    && (a.name.Contains("rocket") || a.name.Contains("missile") || a.name.Contains("projectile") || a.name.Contains("mine") || a.name.Contains("bomb") || a.name.Contains("torpedo")))
                                    .Select(a => a.name).Distinct().OrderBy(n => n).ToList();
                    ShowPicker(T("mbGameFxModel", "One of the game's models"), names.Select(n => (n, (Texture2D)null, (Action)(() => { o["model"] = GameModels.Ref(n); FxEdited(); }))).ToList(), true);
                }, "mbv-step");
                if (ModImport.Available)
                    Act(row, T("mbOwnGlb", "Your .glb…"), () => PickGlb(T("mbPickFxModel", "Choose the model"), path =>
                    {
                        o["model"] = Project.AddModel(path, (it.LocalId ?? "item_" + it.OverrideIndex) + "_" + part);
                        FxEdited();
                    }), "mbv-step");
                Float(fxPage, T("mbSizeUnits", "Size (units; a blaster bolt is ~1600)"), Num("size", 200f), 1f, 100000f, v => Set("size", v));
            }
            // A colour for the recoloured, sprite and model parts.
            var col = o["color"] is JArray ca && ca.Count >= 3 ? new Color((float)ca[0], (float)ca[1], (float)ca[2]) : (Color?)null;
            if (col != null && (col.Value.r > 1f || col.Value.g > 1f || col.Value.b > 1f)) col = new Color(col.Value.r / 255f, col.Value.g / 255f, col.Value.b / 255f);
            Caption(fxPage, T("mbColour", "Colour (0..1)") + (col == null ? "  (" + T("mbAsMade", "as made") + ")" : ""));
            var crow = Row(fxPage, "mbv-xyz");
            var c = col ?? Color.white;
            float R3(float f) => Mathf.Round(f * 1000f) / 1000f;
            Float(crow, "R", c.r, 0f, 1f, v => Set("color", new JArray(R3(v), R3(c.g), R3(c.b))));
            Float(crow, "G", c.g, 0f, 1f, v => Set("color", new JArray(R3(c.r), R3(v), R3(c.b))));
            Float(crow, "B", c.b, 0f, 1f, v => Set("color", new JArray(R3(c.r), R3(c.g), R3(v))));
            if (col != null) Act(crow, "×", () => { o.Remove("color"); FxEdited(); }, "mbv-step");
            Float(fxPage, T("mbGlow", "Glow (above 1 blooms; the game's 2.5)"), Num("glow", 2.5f), 0f, 20f, v => Set("glow", v));
        }

        /// <summary>fx "trail": the smoke trail behind the shots (rockets and missiles have one; any weapon can get one).</summary>
        void BuildTrail(ProjectItem it)
        {
            Heading(fxPage, T("mbTrail", "Trail"));
            var fx = Fx(it, false);
            var t = fx?["trail"];
            int mode = t == null ? 0 : t.Type == JTokenType.Boolean && !(bool)t ? 2 : 1;
            Segments(fxPage, null, new[] { T("mbFxMode_Bases", "Base's"), T("mbOwnTrail", "Own"), T("mbFxMode_None", "None") }, mode, n =>
            {
                if (n == mode) return;
                var f = Fx(it, true);
                if (n == 0) f.Remove("trail");
                else if (n == 1) f["trail"] = new JObject { ["color"] = new JArray(1.0, 0.6, 0.2), ["width"] = 100, ["length"] = 3000 };
                else f["trail"] = false;
                DropEmptyFx(it);
                FxEdited();
            });
            if (!(t is JObject o))
            {
                Note(fxPage, T("mbTrailNote", "Rockets trail smoke, thermo guns a flame; Own gives any weapon one."));
                return;
            }
            float Num(string k, float d) => o[k] is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : d;
            var c = o["color"] is JArray ca && ca.Count >= 3 ? new Color((float)ca[0], (float)ca[1], (float)ca[2]) : Color.white;
            float R3(float f) => Mathf.Round(f * 1000f) / 1000f;
            Caption(fxPage, T("mbTrailColour", "Colour at the start (0..1; it fades to nothing)"));
            var crow = Row(fxPage, "mbv-xyz");
            Float(crow, "R", c.r, 0f, 1f, v => { o["color"] = new JArray(R3(v), R3(c.g), R3(c.b)); FxEdited(false); });
            Float(crow, "G", c.g, 0f, 1f, v => { o["color"] = new JArray(R3(c.r), R3(v), R3(c.b)); FxEdited(false); });
            Float(crow, "B", c.b, 0f, 1f, v => { o["color"] = new JArray(R3(c.r), R3(c.g), R3(v)); FxEdited(false); });
            Float(fxPage, T("mbTrailWidth", "Width (units; a rocket's 100)"), Num("width", 100f), 5f, 2000f, v => { o["width"] = v; FxEdited(false); });
            Float(fxPage, T("mbTrailLength", "Lasts (ms; a rocket's 3000)"), Num("length", 3000f), 100f, 15000f, v => { o["length"] = v; FxEdited(false); });
        }

        void FlagFx(VisualElement parent, string label, bool on, Action<bool> set)
        {
            var b = Act(parent, $"{label}: {(on ? T("mbOn", "on") : T("mbOff", "off"))}", () => { set(!on); BuildFxPage(); });
            b.EnableInClassList("mbv-toggle--on", on);
        }

        // ---- the firing range ---------------------------------------------------------------------------------------

        Gun rangeGun;
        GunRig rangeRig;
        Transform rangeRoot;
        Target drone;
        bool rangePaused;
        int rangeItem = -1;
        float rangeMs;

        /// <summary>The range: the weapon fires from the parked ship's nose at a drone ahead (its distance from the weapon's
        /// reach), the camera between them.</summary>
        void StartRange()
        {
            ShowShooter();
            StopRange();
            int item = FxItemIndex(currentFx);
            var data = item >= 0 ? Database.Shared.Item(item) : null;
            var ship = level.Player != null ? level.Player.transform : null;
            if (data == null || ship == null) return;
            rangeItem = item;
            // Clear of the station: beside it, past its size, facing away from it (the range's shots never reach it).
            float clear = 800f;
            if (level.Station != null)
                foreach (var r in level.Station.GetComponentsInChildren<Renderer>()) clear = Mathf.Max(clear, r.bounds.extents.magnitude + r.bounds.center.magnitude);
            ship.SetPositionAndRotation(new Vector3(clear * 1.3f + 600f, 300f, 0f), Quaternion.LookRotation(Vector3.right, Vector3.up));
            rangeRoot = new GameObject("Mod Builder range").transform;
            rangeGun = new Gun(data, Vector3.zero, data.TypeId == 1);
            var fx = WeaponFx.Load(item);
            rangeRig = new GunRig(rangeGun, fx, rangeRoot, level.Player.visualModel != null ? level.Player.visualModel : ship, 4);
            rangeRig.EnableTrails();
            var rig = rangeRig;
            rangeGun.Hit += (i, t, point) => rig.ShowImpact(point);
            // The drone: a ship model ahead, invulnerable, a big hit cube; its distance by how far the shots reach.
            // The target: another ship facing the shooter, 300-450 m off (as far as the shots reach): the shots are seen
            // flying between them.
            float reach = rangeGun.isBeam ? 7000f : Mathf.Clamp(rangeGun.lifetimeMs * rangeGun.speedUnitsPerMs * 0.7f, 3000f, 9000f);
            var at = ship.position + ship.forward * reach * M;
            var asm = Database.Shared.ShipAssembly(Session.ShipIndex == 28 ? 5 : 28, original: true);
            var prefab = asm != null ? AssembledObject.LoadPrefab(asm) : null;
            var go = prefab != null ? UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(-ship.forward), rangeRoot) : new GameObject("drone");
            go.name = "Mod Builder target drone";
            go.transform.position = at;
            drone = go.AddComponent<Target>();
            drone.radius = 35f;   // metres (a fighter's hit cube, +-1000 units = 50 m, a little tighter: the hull is ~60 m)
            drone.isShip = true;
            drone.hostileToPlayer = true;
            drone.invulnerable = true;
            drone.hp = drone.maxHp = 1e9f;
            rangeGun.AutoAim = () => drone;
            rangeGun.owner = level.Health != null ? level.Health.Target : null;
            // From the side, a little behind the middle: the shooter on the left, the target on the right, the shots between
            // them, both ships in the clear part of the screen between the panels.
            float span = Vector3.Distance(ship.position, at);
            pivot = Vector3.Lerp(ship.position, at, 0.55f);   // the clear part of the screen sits a little left of its middle
            radius = Mathf.Max(60f, span * 0.6f);
            distance = span * 1.25f;
            var side = (ship.right * 0.95f - ship.forward * 0.3f).normalized;
            yaw = Mathf.Atan2(side.x, side.z);
            pitch = 0.18f;
            ApplyAiView();
        }

        /// <summary>The shooter: the Ships tab's ship (the project's) when there is one, else the ship the builder flies.</summary>
        void ShowShooter()
        {
            int want = ModBuilder.CurrentIndex;
            if (level.Player == null || want < 0 || want == Session.ShipIndex && level.Player.visualModel != null) return;
            if (Database.Shared.Ship(want) == null) return;
            ModBuilder.ApplyShip();
            level.SwapPlayerShip(want);
            foreach (var r in level.Player.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        }

        void RestartRange()
        {
            if (kind != ModBuilder.Kind.WeaponFx) return;
            var keep = (pivot, yaw, pitch, distance);
            StartRange();
            if (rangeGun != null) (pivot, yaw, pitch, distance) = keep;
        }

        void StopRange()
        {
            // Its visuals live under rangeRoot: gone with the scene already, nothing is left to hide.
            if (rangeRoot != null) rangeRig?.HideAll();
            rangeGun?.RemoveAll();
            if (rangeRoot != null) UnityEngine.Object.Destroy(rangeRoot.gameObject);
            rangeRoot = null;
            rangeGun = null;
            rangeRig = null;
            drone = null;
            rangeItem = -1;
        }

        void UpdateRange(float dt)
        {
            if (rangeGun == null)
            {
                if (currentFx != null && FxItemIndex(currentFx) >= 0 && !reloading && !reloadPending) StartRange();
                return;
            }
            var ship = level.Player != null ? level.Player.transform : null;
            if (ship == null) return;
            rangeGun.Update(dt, Target.All, drone);
            rangeMs += dt;
            // A pause between bursts so each shot's look reads: a beam held 1.5 s, other weapons one shot a reload (at least 0.35 s).
            if (!rangePaused && rangeGun.reloadAcc >= rangeGun.WaitMs && rangeMs >= (rangeGun.isBeam ? 1500f : 350f))
            {
                var aim = drone != null ? Quaternion.LookRotation(drone.transform.position - ship.position, ship.up) : ship.rotation;
                if (rangeGun.TryFire(ship.position, aim) >= 0)
                {
                    rangeMs = 0f;
                    rangeRig.OnShot();
                    var fx = WeaponFx.Load(rangeItem);
                    var clip = fx != null ? fx.Shot : null;
                    if (clip != null) ShotVoices.PlayAt(clip, ship.position, 0.8f);
                }
            }
            rangeRig.UpdateVisuals(dt, level.mainCamera, ship.forward);
        }
    }
}
