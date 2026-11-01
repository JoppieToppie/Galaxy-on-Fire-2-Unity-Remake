// ModBuilderSpaceViewNpc.cs
// Remake-only: the Mod Builder's NPC ships kind (ModBuilderSpaceView partial; Modding.ModNpcShips, npcships.json): the ships
// nobody flies, new ones (a capital ship, a freighter or a boss on a hull of a ships.json ship or a game NPC hull) and changes
// to the game's (the Terran battleship, the carrier, the Vossk battleship, the freighters, the pirate boss). Left the list,
// right its page: name, kind, hull, race and side, strength, hardpoints, escorts, loot, where it comes, its dock and shop,
// its launches. The hull shows beside the orbit's station (its model as the orbit would build it: Traffic.AssemblyOf) with
// its hardpoints (cyan turrets, amber objects and hangars) and docking pads (red) as the hangar's gizmo draws them
// (World.ModBuilderGizmo): a ball drags over the hull or along its arrows; with "Click the hull to add" on, a click on the
// hull adds one there. Positions are game units in the ship's frame like ships.json's mounts.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderSpaceView
    {
        VisualElement npcList, npcPage;
        ProjectNpcShip currentNpc;
        GameObject npcPreview;
        string npcPreviewKey;
        float npcRetryMs;
        ModBuilderGizmo npcGizmo;
        readonly List<WeaponMount> npcMounts = new List<WeaponMount>();
        bool npcDragging;
        /// <summary>What a click on the hull adds: 0 nothing, 1 a turret, 2 a docking pad.</summary>
        int npcPlace;

        static string[] NpcKindNames => new[] { T("mbNpcCapital", "Capital ship"), T("mbNpcFreighter", "Freighter"), T("mbNpcBoss", "Boss") };
        static string GameNpcName(string key) => key switch
        {
            ModNpcShips.Battleship => T("mbNpcBattleship", "Terran battleship"),
            ModNpcShips.Carrier => T("mbNpcCarrier", "Terran carrier"),
            ModNpcShips.VosskBattleship => T("mbNpcVosskBattleship", "Vossk battleship"),
            ModNpcShips.FreighterKey => T("mbNpcFreighters", "Freighters (every race's)"),
            ModNpcShips.PirateBoss => T("mbNpcPirateBoss", "Pirate boss"),
            _ => key,
        };

        void BuildNpcPanels(VisualElement left, VisualElement right)
        {
            npcList = ListPanel(left, T("mbNpcShips", "NPC ships"));
            Act(left, T("mbNewNpcShip", "+ New NPC ship"), NewNpcShip, "mbv-add");
            Act(left, T("mbChangeNpcShip", "Change a game NPC ship"), ChangeNpcShip, "mbv-add", "mbv-change");
            Act(left, T("mbRemoveNpcShip", "Remove"), AskRemoveNpc, "mbv-danger", "mbv-remove");
            npcPage = PagePanel(right);
        }

        string NpcKey(ProjectNpcShip s) => s.IsOverride ? s.OverrideRef : Project.Key(s.LocalId);
        NpcShipType NpcType(ProjectNpcShip s) => s == null ? null : ModNpcShips.Get(NpcKey(s));
        string NpcTitle(ProjectNpcShip s) => !string.IsNullOrEmpty(s.Name) ? s.Name : s.IsOverride ? GameNpcName(s.OverrideRef) : s.LocalId;

        void ShowNpcKind()
        {
            if (level.Player != null) foreach (var r in level.Player.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            RebuildNpcList();
            BuildNpcPage();
            ShowNpcPreview(true);
        }

        void HideNpcKind()
        {
            if (npcPreview != null) Destroy(npcPreview);
            npcPreview = null;
            npcPreviewKey = null;
            if (npcGizmo != null) npcGizmo.Bind(null, null);
        }

        static void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }

        void RebuildNpcList()
        {
            npcList.Clear();
            if (Project == null) return;
            if (currentNpc != null && !Project.NpcShips.Contains(currentNpc)) currentNpc = null;
            currentNpc ??= Project.NpcShips.FirstOrDefault();
            foreach (var npc in Project.NpcShips)
            {
                var s = npc;
                int kind = Array.IndexOf(ModNpcShips.Kinds, s.Kind);
                string sub = s.IsOverride ? string.Format(T("mbChanges", "Changes {0}"), GameNpcName(s.OverrideRef)) : NpcKindNames[Mathf.Max(0, kind)];
                ListRow(npcList, NpcTitle(s), sub, s == currentNpc, () => { currentNpc = s; RebuildNpcList(); BuildNpcPage(); ShowNpcPreview(true); });
            }
            if (Project.NpcShips.Count == 0)
                Note(npcList, T("mbNoNpcShips", "No NPC ships yet. A new one is a capital ship, a freighter or a boss on a hull of your own or the game's; or change one of the game's."));
        }

        void NewNpcShip()
        {
            var choices = new List<(string, Texture2D, Action)>
            {
                (T("mbNpcOnGameHull", "On a game hull"), null, null),
                (T("mbNpcBattleshipHull", "Capital ship: the Terran battleship's hull"), null, () => NameNew(ModNpcShips.Capital, ModNpcShips.Battleship, null)),
                (T("mbNpcCarrierHull", "Capital ship: the Terran carrier's hull"), null, () => NameNew(ModNpcShips.Capital, ModNpcShips.Carrier, null)),
                (T("mbNpcVosskHull", "Capital ship: the Vossk battleship's hull"), null, () => NameNew(ModNpcShips.Capital, ModNpcShips.VosskBattleship, null)),
                (T("mbNpcFreighterHull", "Freighter: the race's freighter"), null, () => NameNew(ModNpcShips.Freighter, null, null)),
                (T("mbNpcBossHull", "Boss: the pirate boss's ships"), null, () => NameNew(ModNpcShips.Boss, null, null)),
                (T("mbNpcOnShip", "On a ship's hull (a ships.json ship: the project's, another mod's)"), null, null),
                (T("mbNpcCapitalShip", "Capital ship on a ship…"), null, () => PickHull(ModNpcShips.Capital)),
                (T("mbNpcFreighterShip", "Freighter on a ship…"), null, () => PickHull(ModNpcShips.Freighter)),
                (T("mbNpcBossShip", "Boss on a ship…"), null, () => PickHull(ModNpcShips.Boss)),
            };
            ShowPicker(T("mbNewNpcShip", "+ New NPC ship"), choices);
        }

        void PickHull(string kind) => PickRef(RefKind.Ship, T("mbNpcHull", "Its hull"), e => { if (e != null) NameNew(kind, null, e.value); },
                                             e => kind == ModNpcShips.Boss || !int.TryParse(e.value, out int n) || n == 13 || n == 14 || n == 15);

        void NameNew(string kind, string look, string ship) => AskText(T("mbNewNpcShip", "+ New NPC ship"), T("mbName", "Name"), "", name =>
        {
            currentNpc = Project.AddNpcShip(name, kind, look, ship);
            NpcEdited(true);
            RebuildNpcList();
        });

        void ChangeNpcShip()
        {
            var choices = new List<(string, Texture2D, Action)>();
            foreach (var k in ModNpcShips.GameKeys)
            {
                string key = k;
                if (Project.NpcShips.Any(s => s.IsOverride && s.OverrideRef == key)) continue;
                choices.Add((GameNpcName(key), null, () =>
                {
                    currentNpc = Project.AddNpcShipOverride(key);
                    NpcEdited(true);
                    RebuildNpcList();
                }));
            }
            // Another mod's NPC ships (a dependency's): by key.
            var others = ModNpcShips.ModTypes().Where(t => t.mod != null && t.mod.Id != Project.Id && !Project.NpcShips.Any(s => s.IsOverride && s.OverrideRef == t.key)).ToList();
            if (others.Count > 0)
            {
                choices.Add((T("mbNpcOtherMods", "Other mods' (theirs must be on: a dependency)"), null, null));
                foreach (var t in others)
                {
                    string key = t.key;
                    choices.Add(($"{t.name ?? key}\n{key}", null, () => { currentNpc = Project.AddNpcShipOverride(key); NpcEdited(true); RebuildNpcList(); }));
                }
            }
            ShowPicker(T("mbChangeNpcShip", "Change a game NPC ship"), choices);
        }

        void AskRemoveNpc()
        {
            var s = currentNpc;
            if (s == null) return;
            Confirm(T("mbRemoveNpcShip", "Remove"), string.Format(T("mbRemoveText", "Remove \"{0}\" from the project?"), NpcTitle(s)), () =>
            {
                Project.NpcShips.Remove(s);
                currentNpc = null;
                NpcEdited(true);
                RebuildNpcList();
            });
        }

        /// <summary>An edit: saved soon, the game reads the project again (the hull and the preview follow), the page anew.</summary>
        void NpcEdited(bool page = true)
        {
            MarkDirty();
            RequestReload(true);
            if (page) BuildNpcPage();
        }

        // ---- small editors over the entry's JSON ------------------------------------------------------------------

        static float Num(JObject o, string k, float d) => o?[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : d;
        static bool Flag(JObject o, string k, bool d) => o?[k] is JValue v && v.Type == JTokenType.Boolean ? (bool)v : d;
        static void SetNum(JObject o, string k, float? v) { if (v == null) o.Remove(k); else o[k] = Mathf.Approximately(v.Value, Mathf.Round(v.Value)) ? (JToken)(int)Mathf.Round(v.Value) : Math.Round(v.Value, 3); }

        static (int min, int max) RangeOf(JObject o, string k, int d)
        {
            var t = o?[k];
            if (t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)) return ((int)(float)v, (int)(float)v);
            if (t is JArray a && a.Count >= 2) return ((int)(float)a[0], (int)(float)a[1]);
            return (d, d);
        }

        /// <summary>"Label: least [ ] most [ ]" writing n or [least, most] (unset while both are the default 'd' and it was unset).</summary>
        void RangeRow(VisualElement parent, string label, JObject o, string key, int d, int lo, int hi)
        {
            var (a, b) = RangeOf(o, key, d);
            Caption(parent, label);
            var row = Row(parent, "mbv-xyz");
            void Put(int x, int y) { if (y < x) y = x; o[key] = x == y ? (JToken)x : new JArray(x, y); NpcEdited(false); }
            Int(row, T("mbLeast", "Least"), a, lo, hi, v => { a = v; Put(a, b); });
            Int(row, T("mbMost", "Most"), b, lo, hi, v => { b = v; Put(a, b); });
        }

        /// <summary>A list of references ("races", "systems", "ships") as rows with × and an Add… button.</summary>
        void RefList(VisualElement parent, string caption, RefKind k, JObject o, string key, string none)
        {
            Caption(parent, caption);
            var arr = o[key] as JArray;
            if (arr == null || arr.Count == 0) Note(parent, none);
            else
                for (int i = 0; i < arr.Count; i++)
                {
                    int at = i;
                    var row = Row(parent);
                    string v = ProjectEntry.RefText(arr[i]) ?? arr[i].ToString();
                    Add(row, new Label(RefLabel(k, v, v)), "mbv-inline-label");
                    Act(row, "×", () => { arr.RemoveAt(at); if (arr.Count == 0) o.Remove(key); NpcEdited(); }, "mbv-step");
                }
            Act(parent, T("mbAddRef", "+ Add…"), () => PickRef(k, caption, e =>
            {
                if (e == null) return;
                var a = o[key] as JArray ?? new JArray();
                if (a.Any(x => ProjectEntry.RefText(x) == e.value)) return;
                a.Add(ProjectEntry.RefToken(e.value));
                o[key] = a;
                NpcEdited();
            }), "mbv-add");
        }

        /// <summary>A list of { item, amount, chance } rows (loot, the shop's wares).</summary>
        void ItemEntries(VisualElement parent, string caption, JObject o, string key, bool chance)
        {
            Caption(parent, caption);
            var arr = o[key] as JArray;
            if (arr != null)
                for (int i = 0; i < arr.Count; i++)
                {
                    if (!(arr[i] is JObject e)) continue;
                    int at = i;
                    var row = Row(parent);
                    string item = ProjectEntry.RefText(e["item"]);
                    Add(row, new Label(RefLabel(RefKind.Item, item, "?")), "mbv-inline-label");
                    Act(row, "×", () => { arr.RemoveAt(at); if (arr.Count == 0) o.Remove(key); NpcEdited(); }, "mbv-step");
                    RangeRow(parent, T("mbAmount", "Amount"), e, "amount", 1, 1, 9999);
                    if (chance) Float(parent, T("mbChancePct", "Chance (%)"), Num(e, "chance", 100f), 0f, 100f, v => { SetNum(e, "chance", v >= 100f ? (float?)null : v); NpcEdited(false); });
                }
            Act(parent, T("mbAddItem", "+ Add an item…"), () => PickRef(RefKind.Item, caption, e =>
            {
                if (e == null) return;
                var a = o[key] as JArray ?? new JArray();
                a.Add(new JObject { ["item"] = ProjectEntry.RefToken(e.value), ["amount"] = 1 });
                o[key] = a;
                NpcEdited();
            }), "mbv-add");
        }

        /// <summary>A section's on / off ("Escorts of its own: on"): on makes it (with 'make''s first fields), off drops it.</summary>
        bool SectionFlag(VisualElement parent, ProjectNpcShip s, string key, string label, Action<JObject> make = null)
        {
            var o = s.Section(key, false);
            FlagButton(Row(parent), label, o != null, on =>
            {
                if (on) make?.Invoke(s.Section(key, true)); else s.DropSection(key);
            }, () => NpcEdited());
            return o != null;
        }

        // ---- the page --------------------------------------------------------------------------------------------

        void BuildNpcPage()
        {
            npcPage.Clear();
            var s = currentNpc;
            if (s == null) { Note(npcPage, T("mbPickNpcShip", "Add an NPC ship on the left to start.")); return; }
            var t = NpcType(s);
            JsonButton(npcPage, s.Json, () => { NpcEdited(); RebuildNpcList(); },
                       T("mbJsonNpcNote", "Also in npcships.json: every hardpoint field of ships.json's \"subsystems\" (shields, weakspot, stage, hangar ships)."));
            bool isNew = !s.IsOverride;
            string kind = s.Kind;
            bool capital = kind == ModNpcShips.Capital;
            Heading(npcPage, isNew ? T("mbNewNpcSub", "New NPC ship") : string.Format(T("mbChanges", "Changes {0}"), GameNpcName(s.OverrideRef)));
            if (isNew) Note(npcPage, string.Format(T("mbNpcKeyNote", "Other content names it {0}."), NpcKey(s)));
            if (t == null) Note(npcPage, T("mbNpcNotRead", "The game hasn't read it yet (or the messages above say what is wrong)."));
            Text(npcPage, T("mbName", "Name (its lock plate)"), s.Name, 40, v => { s.Name = v.Trim(); NpcEdited(false); RebuildNpcList(); });
            if (isNew)
                Segments(npcPage, T("mbNpcKind", "What it is"), NpcKindNames, Array.IndexOf(ModNpcShips.Kinds, kind), n =>
                {
                    s.Kind = ModNpcShips.Kinds[n];
                    if (s.Kind != ModNpcShips.Capital) { s.DropSection("docking"); s.DropSection("launches"); if (s.LooksLike != ModNpcShips.FreighterKey) s.LooksLike = null; }
                    NpcEdited();
                    RebuildNpcList();
                });
            Note(npcPage, kind == ModNpcShips.Capital ? T("mbNpcCapitalNote", "A capital ship parks in its orbit, its turrets armed; it can be destroyed, carries a crate, fires missile salvos and may take ships aboard.")
                        : kind == ModNpcShips.Freighter ? T("mbNpcFreighterNote", "A freighter cruises through its orbit; pirates hunt it.")
                        : T("mbNpcBossNote", "A boss is a named fighter that comes for the player with its escorts, out where the pirate boss comes."));

            BuildNpcHull(s, t, isNew);
            BuildNpcSide(s, isNew, kind);
            BuildNpcHardpoints(s, t, capital);
            BuildNpcEscorts(s, kind);
            BuildNpcLoot(s);
            BuildNpcSpawn(s, isNew, kind);
            if (capital) BuildNpcDocking(s, t);
            if (capital) BuildNpcLaunches(s);
        }

        void BuildNpcHull(ProjectNpcShip s, NpcShipType t, bool isNew)
        {
            Heading(npcPage, T("mbNpcHullHeading", "Hull"));
            string ship = s.Ship;
            Note(npcPage, ship != null ? string.Format(T("mbNpcShipHull", "The ship {0}'s hull."), RefLabel(RefKind.Ship, ship, ship))
                        : isNew ? string.Format(T("mbNpcGameHull", "The {0}'s hull."), GameNpcName(s.BaseKey))
                        : T("mbNpcOwnHull", "Its own hull."));
            var row = Row(npcPage);
            Act(row, T("mbNpcShipHullPick", "A ship's hull…"), () => PickRef(RefKind.Ship, T("mbNpcHull", "Its hull"), e =>
            {
                if (e == null) return;
                s.Ship = e.value;
                s.LooksLike = null;
                NpcEdited();
            }, e => s.Kind == ModNpcShips.Boss || !int.TryParse(e.value, out int n) || n == 13 || n == 14 || n == 15), "mbv-step");
            if (ship != null) Act(row, T("mbNpcNoShipHull", "No ship's hull"), () => { s.Ship = null; NpcEdited(); }, "mbv-step");
            if (isNew && s.Kind == ModNpcShips.Capital && ship == null)
            {
                var looks = new[] { ModNpcShips.Battleship, ModNpcShips.Carrier, ModNpcShips.VosskBattleship, ModNpcShips.FreighterKey };
                Segments(npcPage, T("mbNpcGameLook", "Game hull"), looks.Select(GameNpcName).ToArray(), Array.IndexOf(looks, s.BaseKey), n => { s.LooksLike = looks[n]; NpcEdited(); });
            }
            if (s.Kind != ModNpcShips.Boss && ship == null && !isNew && s.OverrideRef == ModNpcShips.FreighterKey)
                Note(npcPage, T("mbNpcFreighterLooks", "Each race's freighter keeps its own model unless a ship's hull replaces them all."));
            if (ship != null && s.Kind != ModNpcShips.Boss)
                Note(npcPage, T("mbNpcShipHullNote", "A ship with \"traffic\": { \"capital\" } in ships.json flies on its own too; a ship with no dealer is never sold."));
            Act(npcPage, T("mbNpcFrame", "Frame the hull"), () => FrameNpc(), "mbv-step");
        }

        void BuildNpcSide(ProjectNpcShip s, bool isNew, string kind)
        {
            Heading(npcPage, T("mbNpcSide", "Side and strength"));
            RefField(npcPage, T("mbRace", "Race"), RefKind.Race, s.Race, v => { s.Race = v; NpcEdited(); },
                     isNew ? T("mbNpcSystemRace", "the system's") : T("mbNpcGameRace", "the game's"));
            bool? h = s.Hostile;
            Segments(npcPage, T("mbNpcHostile", "Toward the player"), new[] { T("mbNpcByStanding", "By the standing"), T("mbNpcAlwaysHostile", "Always hostile") },
                     h == true || (h == null && kind == ModNpcShips.Boss) ? 1 : 0,
                     n => { s.Hostile = n == 1 ? (kind == ModNpcShips.Boss ? (bool?)null : true) : (kind == ModNpcShips.Boss ? false : (bool?)null); NpcEdited(); });
            Int(npcPage, T("mbNpcHull", "Hull points (0 = its default; x the difficulty)"), s.Hull ?? 0, 0, 100000000, v => { s.Hull = v > 0 ? v : (int?)null; NpcEdited(false); });
            Float(npcPage, T("mbNpcHullFactor", "Hull x (on its default)"), s.HullFactor, 0.01f, 1000f, v => { s.HullFactor = v; NpcEdited(false); });
            Float(npcPage, T("mbNpcGunFactor", "Guns and turrets: damage x"), s.GunFactor, 0f, 100f, v => { s.GunFactor = v; NpcEdited(false); });
            if (kind != ModNpcShips.Capital)
                Float(npcPage, T("mbNpcSpeed", "Speed (u/ms; 0 = its own: a freighter 1, a boss 3.5)"), s.Speed ?? 0f, 0f, 20f, v => { s.Speed = v > 0f ? v : (float?)null; NpcEdited(false); });
            Float(npcPage, T("mbNpcExplosion", "Explosion size (0 = its own)"), s.ExplosionScale ?? 0f, 0f, 100f, v => { s.ExplosionScale = v > 0f ? v : (float?)null; NpcEdited(false); });
            if (kind == ModNpcShips.Capital)
            {
                var row = Row(npcPage);
                FlagButton(row, T("mbNpcMissiles", "Missile salvos"), s.Missiles, v => s.Missiles = v, () => NpcEdited());
                FlagButton(row, T("mbNpcRetreat", "Jumps away instead of dying"), s.Retreat, v => s.Retreat = v, () => NpcEdited());
            }
        }

        // ---- hardpoints and pads (the gizmo) ----------------------------------------------------------------------

        static readonly string[] HardpointKinds = { "turret", "object", "hangar" };

        void BuildNpcHardpoints(ProjectNpcShip s, NpcShipType t, bool capital)
        {
            Heading(npcPage, T("mbNpcHardpoints", "Hardpoints"));
            var hp = s.Hardpoints(false);
            Note(npcPage, hp == null
                ? T("mbNpcOwnTurrets", "None: its hull's own turrets (the game hull's, or the ship's turret mounts and subsystems). Hardpoints replace them.")
                : T("mbNpcHardpointsNote", "Turrets (cyan), objects and hangars (amber) on the hull. Drag a ball over the hull or along its arrows."));
            Segments(npcPage, T("mbNpcClickAdds", "Click the hull to add"), capital
                     ? new[] { T("mbNothing", "Nothing"), T("mbNpcTurret", "A turret"), T("mbNpcPad", "A docking pad") }
                     : new[] { T("mbNothing", "Nothing"), T("mbNpcTurret", "A turret") }, npcPlace, n => { npcPlace = n; BuildNpcPage(); });
            var row = Row(npcPage);
            Act(row, T("mbNpcAddTurret", "+ Turret on top"), () => AddNpcPoint(1, null, Vector3.up), "mbv-add");
            if (s.BaseKey is string look && (look == ModNpcShips.Battleship || look == ModNpcShips.Carrier || look == ModNpcShips.VosskBattleship) && s.Ship == null)
                Act(row, T("mbNpcGameTurrets", "The game hull's turrets"), () =>
                {
                    var arr = s.Hardpoints(true);
                    arr.Clear();
                    foreach (var p in TrafficPlan.GameHardpoints(look))
                        arr.Add(new JObject { ["position"] = ProjectNpcShip.Xyz(p.position), ["turretUp"] = new JArray(Math.Round(p.up.x, 3), Math.Round(p.up.y, 3), Math.Round(p.up.z, 3)) });
                    NpcEdited();
                    ShowNpcPreview(false);
                }, "mbv-step");
            if (hp != null) Act(row, T("mbNpcHullTurrets", "Back to the hull's own"), () => { s.DropHardpoints(); NpcEdited(); ShowNpcPreview(false); }, "mbv-step");
            if (hp == null) return;
            for (int i = 0; i < hp.Count; i++)
            {
                if (!(hp[i] is JObject p)) continue;
                int at = i;
                string k = (string)p["kind"] ?? "turret";
                bool on = npcGizmo != null && npcGizmo.Selected == at;
                var line = Row(npcPage);
                var pos = ProjectNpcShip.Xyz(p["position"] ?? p["position_engine"]) ?? Vector3.zero;
                Act(line, $"{at + 1}. {KindLabel(k)}  ({pos.x:0}, {pos.y:0}, {pos.z:0})", () => { SelectNpcPoint(at); }, on ? "mbv-toggle--on" : "mbv-step");
                Act(line, "×", () => { hp.RemoveAt(at); if (npcGizmo != null) npcGizmo.Selected = -1; NpcEdited(); ShowNpcPreview(false); }, "mbv-step");
                if (!on) continue;
                Segments(npcPage, null, HardpointKinds.Select(KindLabel).ToArray(), Array.IndexOf(HardpointKinds, k), n =>
                {
                    if (n == 0) p.Remove("kind"); else p["kind"] = HardpointKinds[n];
                    if (n == 2 && !(p["ships"] is JArray)) p["ships"] = new JArray(5);
                    NpcEdited();
                    ShowNpcPreview(false);
                });
                var xyz = Row(npcPage, "mbv-xyz");
                for (int c = 0; c < 3; c++)
                {
                    int axis = c;
                    Float(xyz, "XYZ".Substring(axis, 1), pos[axis], -1000000f, 1000000f, v =>
                    {
                        var a = ProjectNpcShip.Xyz(p["position"] ?? p["position_engine"]) ?? Vector3.zero;
                        a[axis] = v;
                        p.Remove("position_engine");
                        p["position"] = ProjectNpcShip.Xyz(a);
                        NpcEdited(false);
                        ShowNpcPreview(false);
                    });
                }
                if (k == "turret")
                {
                    RefField(npcPage, T("mbNpcTurretItem", "Turret item"), RefKind.Item, ProjectEntry.RefText(p["item"]), v =>
                    {
                        if (v == null) p.Remove("item"); else p["item"] = ProjectEntry.RefToken(v);
                        NpcEdited();
                    }, T("mbNpcGameTurret", "the game's turret"), filter: e => e.type < 0 || e.type == 2);
                    bool down = p["turretUp"] is JArray up && up.Count >= 3 && (float)up[1] < -0.5f || Flag(p, "upsideDown", false);
                    FlagButton(Row(npcPage), T("mbUpsideDown", "Upside down"), down, v => { p.Remove("turretUp"); if (v) p["upsideDown"] = true; else p.Remove("upsideDown"); }, () => { NpcEdited(); ShowNpcPreview(false); });
                }
                else
                {
                    Text(npcPage, T("mbNpcPartModel", "Model (\"gof2:<assembly>\", a ship, or none)"), (string)p["model"], 80, v =>
                    {
                        if (string.IsNullOrWhiteSpace(v)) p.Remove("model"); else p["model"] = v.Trim();
                        NpcEdited(false);
                    });
                }
                Int(npcPage, T("mbNpcPartHull", "Hull points (0 = by rank)"), (int)Num(p, "hull", 0), 0, 10000000, v => { SetNum(p, "hull", v > 0 ? v : (float?)null); NpcEdited(false); });
                Text(npcPage, T("mbName", "Name"), (string)p["name"], 40, v => { if (string.IsNullOrWhiteSpace(v)) p.Remove("name"); else p["name"] = v.Trim(); NpcEdited(false); });
                if (k == "hangar")
                {
                    RefList(npcPage, T("mbNpcHangarShips", "Ships it launches"), RefKind.Ship, p, "ships", T("mbNone", "none"));
                    Float(npcPage, T("mbNpcEvery", "Every (seconds)"), Num(p, "every", 25f), 3f, 3600f, v => { SetNum(p, "every", v); NpcEdited(false); });
                    Int(npcPage, T("mbNpcPerWave", "Per wave"), (int)Num(p, "perWave", 2), 1, 20, v => { SetNum(p, "perWave", v); NpcEdited(false); });
                    Int(npcPage, T("mbNpcMaxAlive", "At most flying"), (int)Num(p, "maxAlive", 6), 1, 50, v => { SetNum(p, "maxAlive", v); NpcEdited(false); });
                }
            }
        }

        static string KindLabel(string k) => k == "object" ? T("mbNpcObject", "Object") : k == "hangar" ? T("mbNpcHangar", "Hangar") : T("mbNpcTurret", "Turret");

        void SelectNpcPoint(int index)
        {
            if (npcGizmo == null) return;
            npcGizmo.Selected = index;
            npcGizmo.Refresh();
            BuildNpcPage();
        }

        /// <summary>A new hardpoint (what 1) or pad (2) at 'game' (null: on top of the hull's box), selected.</summary>
        void AddNpcPoint(int what, Vector3? game, Vector3 upGame)
        {
            var s = currentNpc;
            if (s == null) return;
            if (game == null)
            {
                var b = npcGizmo != null && npcPreview != null ? npcGizmo.LocalBounds : new Bounds(Vector3.zero, Vector3.one * 100f);
                game = new Vector3(-b.center.x, b.max.y, b.center.z) / M;
            }
            if (what == 2)
            {
                var dock = s.Section("docking", true);
                var pads = dock["pads"] as JArray ?? new JArray();
                pads.Add(new JObject { ["position"] = ProjectNpcShip.Xyz(game.Value) });
                dock["pads"] = pads;
                if (npcGizmo != null) npcGizmo.Selected = (s.Hardpoints(false)?.Count ?? 0) + pads.Count - 1;
            }
            else
            {
                var arr = s.Hardpoints(true);
                var o = new JObject { ["position"] = ProjectNpcShip.Xyz(game.Value) };
                if (upGame.y < 0.7f) o["turretUp"] = new JArray(Math.Round(upGame.x, 3), Math.Round(upGame.y, 3), Math.Round(upGame.z, 3));
                arr.Add(o);
                if (npcGizmo != null) npcGizmo.Selected = arr.Count - 1;
            }
            NpcEdited();
            ShowNpcPreview(false);
        }

        /// <summary>The gizmo's list from the entry: its hardpoints (turrets cyan, the rest amber), then its pads (red).</summary>
        void FillNpcMounts()
        {
            npcMounts.Clear();
            var s = currentNpc;
            if (s == null) return;
            foreach (var p in (s.Hardpoints(false) ?? new JArray()).OfType<JObject>())
            {
                var pos = ProjectNpcShip.Xyz(p["position"] ?? p["position_engine"]) ?? Vector3.zero;
                var m = new WeaponMount { slotType = ((string)p["kind"] ?? "turret") == "turret" ? 2 : 1, position_engine = new[] { (int)pos.x, (int)pos.y, (int)pos.z } };
                if (p["turretUp"] is JArray up && up.Count >= 3) m.turretUp = new[] { (float)up[0], (float)up[1], (float)up[2] };
                else if (Flag(p, "upsideDown", false)) m.turretUp = new[] { 0f, -1f, 0f };
                npcMounts.Add(m);
            }
            foreach (var p in s.Pads)
            {
                var pos = ProjectNpcShip.Xyz(p["position"]) ?? Vector3.zero;
                npcMounts.Add(new WeaponMount { slotType = 0, position_engine = new[] { (int)pos.x, (int)pos.y, (int)pos.z } });
            }
        }

        /// <summary>A gizmo point moved: its place (and a turret's facing) back into the entry.</summary>
        void OnNpcPointMoved(int i)
        {
            var s = currentNpc;
            if (s == null || i < 0 || i >= npcMounts.Count) return;
            var m = npcMounts[i];
            var pos = new Vector3(m.position_engine[0], m.position_engine[1], m.position_engine[2]);
            var hp = s.Hardpoints(false);
            int n = hp?.Count ?? 0;
            if (i < n && hp[i] is JObject p)
            {
                p.Remove("position_engine");
                p["position"] = ProjectNpcShip.Xyz(pos);
                if (m.slotType == 2 && m.turretUp != null && m.turretUp.Length >= 3)
                {
                    p.Remove("upsideDown");
                    p["turretUp"] = new JArray(Math.Round(m.turretUp[0], 3), Math.Round(m.turretUp[1], 3), Math.Round(m.turretUp[2], 3));
                }
            }
            else if (s.Pads.ElementAtOrDefault(i - n) is JObject pad)
            {
                var old = ProjectNpcShip.Xyz(pad["position"]) ?? pos;
                pad["position"] = ProjectNpcShip.Xyz(pos);
                if (ProjectNpcShip.Xyz(pad["approach"]) is Vector3 ap) pad["approach"] = ProjectNpcShip.Xyz(ap + (pos - old));   // the approach moves along
            }
            MarkDirty();
        }

        // ---- escorts, loot, spawn, docking, launches --------------------------------------------------------------

        void BuildNpcEscorts(ProjectNpcShip s, string kind)
        {
            Heading(npcPage, T("mbNpcEscorts", "Escorts"));
            if (!SectionFlag(npcPage, s, "escorts", T("mbNpcOwnEscorts", "Escorts of its own"), o => o["count"] = new JArray(2, 4)))
            {
                Note(npcPage, s.IsOverride ? T("mbNpcGameEscorts", "The game's escorts (the enhanced capital ships', the boss's).") : T("mbNpcNoEscorts", "None."));
                return;
            }
            var e = s.Section("escorts", true);
            RangeRow(npcPage, T("mbNpcEscortCount", "How many"), e, "count", 2, 0, 30);
            Float(npcPage, T("mbNpcEscortStrength", "Strength (their hull and guns x)"), Num(e, "strength", 1f), 0.05f, 50f, v => { SetNum(e, "strength", Mathf.Approximately(v, 1f) ? (float?)null : v); NpcEdited(false); });
            RefList(npcPage, T("mbNpcEscortShips", "Their ships"), RefKind.Ship, e, "ships", T("mbNpcStrongFighters", "the dearer half of its race's fighters"));
            RefField(npcPage, T("mbNpcEscortRace", "Their race"), RefKind.Race, (string)e["race"], v => { if (v == null) e.Remove("race"); else e["race"] = v; NpcEdited(); }, T("mbNpcHostRace", "its own"));
            Note(npcPage, kind == ModNpcShips.Capital ? T("mbNpcEscortLoop", "They loop round it.") : kind == ModNpcShips.Freighter ? T("mbNpcEscortFollow", "They follow it in formation and fight what attacks it.") : T("mbNpcEscortBoss", "They fly with it."));
        }

        void BuildNpcLoot(ProjectNpcShip s)
        {
            Heading(npcPage, T("mbNpcLoot", "Loot"));
            int mode = s.NoLoot ? 2 : s.Section("loot", false) != null ? 1 : 0;
            Segments(npcPage, null, new[] { T("mbNpcGameLoot", "The game's"), T("mbNpcOwnLoot", "Its own"), T("mbNpcNoLoot", "Nothing") }, mode, n =>
            {
                if (n == 0) s.DropSection("loot");
                else if (n == 2) s.NoLoot = true;
                else { s.DropSection("loot"); s.Section("loot", true); }
                NpcEdited();
            });
            if (mode != 1) return;
            var l = s.Section("loot", true);
            FlagButton(Row(npcPage), T("mbNpcKeepLoot", "The game's loot too"), Flag(l, "keep", false), v => { if (v) l["keep"] = true; else l.Remove("keep"); }, () => NpcEdited());
            ItemEntries(npcPage, T("mbNpcLootItems", "Items (each by its chance)"), l, "items", true);
            RangeRow(npcPage, T("mbNpcCargoRolls", "A freighter's cargo rolls"), l, "cargoRolls", 0, 0, 10);
        }

        void BuildNpcSpawn(ProjectNpcShip s, bool isNew, string kind)
        {
            Heading(npcPage, T("mbNpcSpawn", "Where and how often"));
            var sp = s.Section("spawn", false);
            if (!isNew)
            {
                bool own = SectionFlag(npcPage, s, "spawn", T("mbNpcOwnChance", "A chance of its own"), o => o["chance"] = 30);
                if (!own) { Note(npcPage, T("mbNpcGameChance", "The game's: 30 % of the orbits with freighters of its race; freighters 0-4 an orbit.")); return; }
                sp = s.Section("spawn", true);
                if (s.OverrideRef != ModNpcShips.PirateBoss)
                    Float(npcPage, T("mbNpcChance", "Chance (% of the orbits it may come to)"), Num(sp, "chance", 30f), 0f, 100f, v => { SetNum(sp, "chance", v); NpcEdited(false); });
                if (s.OverrideRef == ModNpcShips.FreighterKey) RangeRow(npcPage, T("mbNpcCount", "How many an orbit"), sp, "count", 0, 0, 20);
                if (s.OverrideRef == ModNpcShips.PirateBoss) Note(npcPage, T("mbNpcBossChance", "The pirate events decide when the boss comes."));
                return;
            }
            sp ??= s.Section("spawn", true);
            Float(npcPage, T("mbNpcChance", "Chance (% of the orbits it may come to)"), Num(sp, "chance", 0f), 0f, 100f, v => { SetNum(sp, "chance", v); NpcEdited(false); });
            RangeRow(npcPage, T("mbNpcCount", "How many an orbit"), sp, "count", 1, 0, 20);
            RefList(npcPage, T("mbNpcSpawnRaces", "In systems of these races"), RefKind.Race, sp, "races", T("mbNpcAnyRace", "any"));
            RefList(npcPage, T("mbNpcSpawnSystems", "Only in these systems"), RefKind.System, sp, "systems", T("mbNpcAnySystem", "any"));
            RefList(npcPage, T("mbNpcSpawnStations", "Only at these stations"), RefKind.Station, sp, "stations", T("mbNpcAnyStation", "any"));
            Caption(npcPage, T("mbNpcSecurity", "Security levels (none lit = any)"));
            var secRow = Row(npcPage);
            var sec = (sp["security"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>();
            string[] secNames = { T("mbSecDangerous", "Dangerous"), T("mbSecRisky", "Risky"), T("mbSecAverage", "Average"), T("mbSecSecure", "Secure") };
            for (int lvl = 0; lvl < 4; lvl++)
            {
                int l = lvl;
                var b = Act(secRow, secNames[l], () =>
                {
                    if (sec.Contains(l)) sec.Remove(l); else sec.Add(l);
                    if (sec.Count == 0) sp.Remove("security"); else sp["security"] = new JArray(sec.OrderBy(x => x));
                    NpcEdited();
                }, "mbv-seg");
                b.EnableInClassList("mbv-seg--on", sec.Contains(l));
            }
            var ranks = Row(npcPage, "mbv-xyz");
            Int(ranks, T("mbNpcMinRank", "Least rank"), (int)Num(sp, "minRank", 0), 0, 100, v => { SetNum(sp, "minRank", v > 0 ? v : (float?)null); NpcEdited(false); });
            Int(ranks, T("mbNpcMaxRank", "Most rank (0 = any)"), (int)Num(sp, "maxRank", 0), 0, 100, v => { SetNum(sp, "maxRank", v > 0 ? v : (float?)null); NpcEdited(false); });
            var steps = Row(npcPage, "mbv-xyz");
            Int(steps, T("mbNpcMinStep", "From story step (-1 any)"), (int)Num(sp, "minStep", -1), -1, Story.LastIndex, v => { SetNum(sp, "minStep", v >= 0 ? v : (float?)null); NpcEdited(false); });
            Int(steps, T("mbNpcMaxStep", "Up to step (-1 any)"), (int)Num(sp, "maxStep", -1), -1, Story.LastIndex, v => { SetNum(sp, "maxStep", v >= 0 ? v : (float?)null); NpcEdited(false); });
            var flags = Row(npcPage);
            FlagButton(flags, T("mbNpcFreePlayOnly", "Free play only"), Flag(sp, "freePlayOnly", false), v => { if (v) { sp["freePlayOnly"] = true; sp.Remove("storyOnly"); } else sp.Remove("freePlayOnly"); }, () => NpcEdited());
            FlagButton(flags, T("mbNpcStoryOnly", "During the story only"), Flag(sp, "storyOnly", false), v => { if (v) { sp["storyOnly"] = true; sp.Remove("freePlayOnly"); } else sp.Remove("storyOnly"); }, () => NpcEdited());
            if (kind == ModNpcShips.Capital)
                FlagButton(Row(npcPage), T("mbNpcReplaceGame", "Only in the game's capital ship's place"), Flag(sp, "replaceGame", false), v => { if (v) sp["replaceGame"] = true; else sp.Remove("replaceGame"); }, () => NpcEdited());
            Text(npcPage, T("mbNpcCondition", "Condition (the missions' expressions, e.g. quest(my_quest) == done)"), (string)sp["condition"], 200, v =>
            {
                if (string.IsNullOrWhiteSpace(v)) sp.Remove("condition"); else sp["condition"] = v.Trim();
                NpcEdited(false);
            });
            RangeRow(npcPage, T("mbNpcDistance", "Distance from the station (km; 0 = its kind's place)"), sp, "distance", 0, 0, 500);
            FlagButton(Row(npcPage), T("mbNpcAtPlace", "At a place of my own"), sp["position"] != null, on => { if (on) sp["position"] = new JArray(0, 0, 100000); else sp.Remove("position"); }, () => NpcEdited());
            if (sp["position"] != null) XyzRow(npcPage, T("mbNpcPlace", "At (game units from the station: z ahead)"), sp, "position", () => NpcEdited(false));
        }

        void BuildNpcDocking(ProjectNpcShip s, NpcShipType t)
        {
            Heading(npcPage, T("mbNpcDocking", "Dock and shop"));
            bool gameDeck = s.IsOverride && (s.OverrideRef == ModNpcShips.Carrier || s.OverrideRef == ModNpcShips.VosskBattleship);
            if (!SectionFlag(npcPage, s, "docking", T("mbNpcTakesShips", "Takes ships aboard (resupply and a shop)")))
            {
                Note(npcPage, gameDeck ? T("mbNpcGameDock", "The game's: with the capital ship enhancements on, it resupplies pilots its race trusts.") : T("mbNpcNoDock", "No docking."));
                return;
            }
            var d = s.Section("docking", true);
            int pads = s.Pads.Count();
            Note(npcPage, pads > 0 ? string.Format(T("mbNpcPadsN", "{0} docking pads (red balls): a ship lands on the nearest free one."), pads)
                : s.Ship == null && (s.BaseKey == ModNpcShips.Carrier || s.BaseKey == ModNpcShips.VosskBattleship) ? T("mbNpcGamePads", "The game hull's own pads. Add pads to place your own.")
                : T("mbNpcNeedPads", "Add docking pads: \"Click the hull to add: a docking pad\", then click its deck."));
            var row = Row(npcPage);
            if (s.Ship == null && (s.BaseKey == ModNpcShips.Carrier || s.BaseKey == ModNpcShips.VosskBattleship))
                Act(row, T("mbNpcGamePadsCopy", "The game's pads"), () =>
                {
                    var pts = SpacePoints.Set(s.BaseKey == ModNpcShips.Carrier ? CapitalShips.DeckPoints : CapitalShips.VosskDeckPoints);
                    var arr = new JArray();
                    foreach (var dock in pts.Where(p => p.type == SpacePoints.Dock))
                    {
                        var ap = pts.Where(p => p.type == SpacePoints.Approach).OrderBy(p => (p.engine - dock.engine).sqrMagnitude).Select(p => (Vector3?)p.engine).FirstOrDefault();
                        var o = new JObject { ["position"] = ProjectNpcShip.Xyz(dock.engine), ["facing"] = new JArray(Math.Round(dock.dir.x, 3), Math.Round(dock.dir.y, 3), Math.Round(dock.dir.z, 3)) };
                        if (ap != null) o["approach"] = ProjectNpcShip.Xyz(ap.Value);
                        arr.Add(o);
                    }
                    d["pads"] = arr;
                    NpcEdited();
                    ShowNpcPreview(false);
                }, "mbv-step");
            if (pads > 0) Act(row, T("mbNpcNoPads", "Remove the pads"), () => { d.Remove("pads"); NpcEdited(); ShowNpcPreview(false); }, "mbv-danger");
            Int(npcPage, T("mbNpcStanding", "Least standing with its race (-100..100; friends from 71)"), (int)Num(d, "standing", 71), -100, 100, v => { SetNum(d, "standing", v == 71 ? (float?)null : v); NpcEdited(false); });
            var flags = Row(npcPage);
            FlagButton(flags, T("mbNpcRaceShip", "Only in a ship of its race"), Flag(d, "raceShip", true), v => { if (v) d.Remove("raceShip"); else d["raceShip"] = false; }, () => NpcEdited());
            FlagButton(flags, T("mbNpcSignature", "A signature of its race lets anyone in"), Flag(d, "signature", true), v => { if (v) d.Remove("signature"); else d["signature"] = false; }, () => NpcEdited());
            Heading(npcPage, T("mbNpcShop", "Its shop"));
            FlagButton(Row(npcPage), T("mbNpcRepair", "Repairs hull and armour"), Flag(d, "repair", true), v => { if (v) d.Remove("repair"); else d["repair"] = false; }, () => NpcEdited());
            if (Flag(d, "repair", true))
                Int(npcPage, T("mbNpcRepairPrice", "Repair price (credits a point)"), (int)Num(d, "repairPrice", 10), 0, 100000, v => { SetNum(d, "repairPrice", v == 10 ? (float?)null : v); NpcEdited(false); });
            Float(npcPage, T("mbNpcMarkup", "Prices x the orbit station's"), Num(d, "markup", 1.25f), 0f, 100f, v => { SetNum(d, "markup", Mathf.Approximately(v, 1.25f) ? (float?)null : v); NpcEdited(false); });
            RangeRow(npcPage, T("mbNpcCells", "Energy cells in stock"), d, "energyCells", 10, 0, 9999);
            RangeRow(npcPage, T("mbNpcAmmo", "Rounds of each secondary of its race"), d, "ammo", 5, 0, 9999);
            Int(npcPage, T("mbNpcGear", "Weapons and equipment of its race (-1 = the game's 5 / 6)"), (int)Num(d, "gear", -1), -1, 30, v => { SetNum(d, "gear", v >= 0 ? v : (float?)null); NpcEdited(false); });
            ItemEntries(npcPage, T("mbNpcSells", "Also sells (any item, by its chance)"), d, "sells", true);
        }

        void BuildNpcLaunches(ProjectNpcShip s)
        {
            Heading(npcPage, T("mbNpcLaunches", "Launches fighters"));
            if (!SectionFlag(npcPage, s, "launches", T("mbNpcLaunchesOn", "Launches fighters when attacked"), o => o["ships"] = new JArray(5)))
            {
                Note(npcPage, s.IsOverride && (s.OverrideRef == ModNpcShips.Carrier || s.OverrideRef == ModNpcShips.VosskBattleship)
                    ? T("mbNpcGameLaunches", "The game's: with the enhancements on, 5 every 20 s, 15 in all.") : T("mbNpcNoLaunches", "None (a hangar hardpoint launches on its own)."));
                return;
            }
            var l = s.Section("launches", true);
            RefList(npcPage, T("mbNpcLaunchShips", "Its fighters"), RefKind.Ship, l, "ships", T("mbNpcLaunchDefault", "the carrier's Inflicts"));
            Int(npcPage, T("mbNpcPerWave", "Per wave"), (int)Num(l, "perWave", 5), 1, 20, v => { SetNum(l, "perWave", v == 5 ? (float?)null : v); NpcEdited(false); });
            Int(npcPage, T("mbNpcTotal", "In all"), (int)Num(l, "total", 15), 0, 200, v => { SetNum(l, "total", v == 15 ? (float?)null : v); NpcEdited(false); });
            Float(npcPage, T("mbNpcEvery", "Every (seconds)"), Num(l, "every", 20f), 3f, 3600f, v => { SetNum(l, "every", Mathf.Approximately(v, 20f) ? (float?)null : v); NpcEdited(false); });
            Note(npcPage, T("mbNpcLaunchFrom", "They leave from its docking pads, else from the top of its hull."));
        }

        // ---- the preview ------------------------------------------------------------------------------------------

        /// <summary>The spec the orbit would build its hull from (Traffic.AssemblyOf), without its rolls.</summary>
        static SpawnSpec PreviewSpec(ProjectNpcShip s, NpcShipType t)
        {
            int ship = t?.ship ?? -1;
            if (ship < 0 && s.Ship != null) ModContent.TryResolveShip(s.Ship, out ship);
            int race = t != null && t.race >= 0 ? t.race : 0;
            string kind = s.Kind;
            if (ship >= 0 && CustomShips.IsCustom(ship)) return new SpawnSpec { ship = ship, race = race, freighter = kind != ModNpcShips.Boss, modCapital = kind != ModNpcShips.Boss };
            if (kind == ModNpcShips.Boss) return new SpawnSpec { ship = ship >= 0 ? ship : 60, race = Standing.Pirate };
            if (ship == 13 || ship == 14 || ship == 15) return new SpawnSpec { ship = ship, race = race, freighter = true };
            return s.BaseKey switch
            {
                ModNpcShips.Battleship => new SpawnSpec { ship = 14, race = race, freighter = true },
                ModNpcShips.Carrier => new SpawnSpec { ship = -1, fixedObject = "sn_carrier_terran_1" },
                ModNpcShips.VosskBattleship => new SpawnSpec { ship = -1, fixedObject = "sn_battleship_vossk" },
                _ => new SpawnSpec { ship = race == 1 ? 13 : 15, race = race, freighter = true },
            };
        }

        /// <summary>The current entry's hull beside the station (built again when its look changed) and its points.</summary>
        void ShowNpcPreview(bool frame)
        {
            if (kind != ModBuilder.Kind.NpcShips) return;
            var s = currentNpc;
            if (s == null) { HideNpcKind(); return; }
            if (npcGizmo == null)
            {
                npcGizmo = new GameObject("NPC ship gizmo").AddComponent<ModBuilderGizmo>();
                npcGizmo.Moved += OnNpcPointMoved;
            }
            npcGizmo.cam = level.mainCamera;
            var spec = PreviewSpec(s, NpcType(s));
            var db = Database.Shared;
            var entry = Traffic.AssemblyOf(db, spec);
            string key = (entry?.name ?? "?") + "|" + s.Ref;
            if (key != npcPreviewKey || npcPreview == null)
            {
                Destroy(npcPreview);
                npcPreview = null;
                var prefab = AssembledObject.LoadPrefab(entry);
                if (prefab == null) { npcPreviewKey = null; npcRetryMs = 500f; npcGizmo.Bind(null, null); return; }   // a mod hull still building: again soon
                npcPreviewKey = key;
                npcPreview = new GameObject("NPC ship preview");
                npcPreview.transform.rotation = spec.fixedObject != null ? OrbitLayout.RotationToUnity(Vector3.zero) : Quaternion.Euler(0f, 180f, 0f);
                var model = UnityEngine.Object.Instantiate(prefab, npcPreview.transform, false);
                model.GetComponent<AssembledObject>()?.SetPlayerVariant(false);
                // Beside the station, clear of it.
                var hull = HullBounds(npcPreview);
                var st = level.Station != null ? HullBounds(level.Station) : new Bounds(Vector3.zero, Vector3.one * 200f);
                npcPreview.transform.position = st.center + Vector3.left * (st.extents.magnitude + hull.extents.magnitude + 200f) - (hull.center - npcPreview.transform.position);
                frame = true;
            }
            FillNpcMounts();
            npcGizmo.Bind(npcPreview.transform, npcMounts);
            if (frame) FrameNpc();
        }

        static Bounds HullBounds(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.one);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        void FrameNpc()
        {
            if (npcPreview == null) return;
            var b = HullBounds(npcPreview);
            pivot = b.center;
            radius = Mathf.Clamp(b.extents.magnitude, 20f, 20000f);
            distance = radius * 2.4f;
            ApplyAiView();
        }

        /// <summary>Per frame in this kind: a mod hull still loading is tried again; the handles' hover.</summary>
        void UpdateNpc(float dt)
        {
            if (npcPreview == null && currentNpc != null && (npcRetryMs -= dt) <= 0f) { npcRetryMs = 500f; ShowNpcPreview(true); }
            if (npcGizmo != null && !npcDragging && Pointer.current != null) npcGizmo.Hover(ScreenPos());
        }

        /// <summary>A press on the view in this kind: the gizmo's handle or ball, or (with "Click the hull to add") a new point
        /// on the hull; false = the camera's (orbit).</summary>
        bool NpcPointerDown(int button)
        {
            if (button != 0 || npcGizmo == null || npcPreview == null) return false;
            var p = ScreenPos();
            if (npcGizmo.BeginDrag(p)) { npcDragging = true; BuildNpcPage(); return true; }
            if (npcPlace != 0 && npcGizmo.RaycastHull(p, out var world, out var normal))
            {
                var g = npcGizmo.GameOf(world + normal * 0.5f);
                AddNpcPoint(npcPlace, new Vector3(g.x, g.y, g.z), npcGizmo.GameDirOf(normal));
                return true;
            }
            return false;
        }

        void NpcPointerMove()
        {
            if (!npcDragging || npcGizmo == null) return;
            var kb = Keyboard.current;
            npcGizmo.Drag(ScreenPos(), kb != null && kb.ctrlKey.isPressed, 100);
        }

        void NpcPointerUp()
        {
            if (!npcDragging) return;
            npcDragging = false;
            npcGizmo?.EndDrag();
            NpcEdited();
        }

        void DisposeNpc()
        {
            HideNpcKind();
            if (npcGizmo != null) UnityEngine.Object.Destroy(npcGizmo.gameObject);
            npcGizmo = null;
        }
    }
}
