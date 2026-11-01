// ModBuilderViewPages.cs
// ModBuilderView: the pages (part of the partial ModBuilderView; its fields are in ModBuilderView.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        void SelectTab(int n)
        {
            tab = n;
            for (int i = 0; i < 3; i++) tabs[i].EnableInClassList("mbv-tab--on", i == n);
            ship.style.display = n == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            hardpoints.style.display = n == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            loadout.style.display = n == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            if (n != 1) placing = -1;
        }
        void RebuildShips()
        {
            shipList.Clear();
            if (Project == null) return;
            var db = Database.Shared;
            foreach (var s in Project.Ships)
            {
                var row = new Button(() => { sound(false); SelectShip(s); }) { focusable = false };
                row.AddToClassList("mbv-ship-row");
                row.EnableInClassList("mbv-ship-row--on", s == Current);
                int index = ModBuilder.IndexOf(s);
                var icon = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-ship-icon");
                var tex = index >= 0 ? ItemInfo.ShipIcon(index) : null;
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                var text = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-ship-text");
                Add(text, new Label(ShipName(s, db)) { pickingMode = PickingMode.Ignore }, "mbv-ship-name");
                Add(text, new Label(s.IsOverride ? string.Format(T("mbChanges", "Changes {0}"), GameNames.Ship(s.OverrideIndex))
                                    : s.IsKitShip ? T("mbKitShipSub", "Customizable ship") : T("mbNewShipSub", "New ship"))
                    { pickingMode = PickingMode.Ignore }, "mbv-ship-sub");
                shipList.Add(row);
            }
            if (Project.Ships.Count == 0)
                Note(shipList, T("mbNoShips", "No ships yet. Add one from a .glb model, or change a game ship."));
        }
        static string ShipName(ProjectShip s, Database db) =>
            !string.IsNullOrEmpty(s.Name) ? s.Name : s.IsOverride ? GameNames.Ship(s.OverrideIndex) : s.LocalId;
        void SelectShip(ProjectShip s)
        {
            if (s == Current) return;
            SaveNow();
            ModBuilder.Ship = s;
            Project.LastShip = s?.Ref;
            gizmo.Selected = -1;
            placing = -1;
            ModBuilder.ApplyShip();
            boundShip = -2;   // the turntable swaps to it (Update)
            RebuildShips();
            RebuildPages();
            MarkDirty();
        }
        void RebuildPages()
        {
            BuildShipPage();
            BuildHardpointsPage();
            BuildLoadoutPage();
        }
        // Ship ------------------------------------------------------------------------------------------------------

        void BuildShipPage()
        {
            ship.Clear();
            var s = Current;
            if (s == null) { Note(ship, T("mbPickShip", "Add a ship on the left to start.")); return; }
            JsonButton(ship, s.Json, () => { s.ReadMounts(); SaveNow(); RequestReload(false); RebuildShips(); BuildShipPage(); BuildHardpointsPage(); },
                       T("mbJsonShipNote", "Also in ships.json: materials, glows, dealer, lounge, npcTurrets, available."));
            var db = Database.Shared;
            var original = s.IsOverride ? db.Ship(s.OverrideIndex) : null;
            Heading(ship, s.IsOverride ? string.Format(T("mbChanges", "Changes {0}"), GameNames.Ship(s.OverrideIndex)) : T("mbNewShipSub", "New ship"));
            var name = Text(ship, T("mbName", "Name"), s.Name, 40, v => { s.Name = v.Trim(); RebuildShips(); MarkDirty(); });
            if (s.IsOverride) name.textEdition.placeholder = GameNames.Ship(s.OverrideIndex);
            // Remake: the dim tag after its name in the lists (ships.json "tag"; empty = the mod's Ship tag, Mod details)
            var tag = Text(ship, T("mbShipTag", "Tag after the name"), s.Tag, 24, v => { s.Tag = v.Trim(); MarkDirty(); });
            tag.textEdition.placeholder = Project.ShipTag;
            Text(ship, T("mbDescription", "Description"), s.Description, 600, v => { s.Description = v; MarkDirty(); }, true);
            if (!s.IsOverride) ShipRaceField(s);   // a game race or a mod's own (ModBuilderViewRaces)
            Heading(ship, T("mbStats", "Stats"));
            Int(ship, T("mbArmor", "Hull points"), s.Armor, 1, 100000, v => { s.Armor = v; MarkDirty(); });
            Int(ship, T("mbCargo", "Cargo (t)"), s.Cargo, 0, 100000, v => { s.Cargo = v; MarkDirty(); });
            Int(ship, T("mbHandling", "Handling"), s.Handling, 1, 400, v => { s.Handling = v; MarkDirty(); });
            Int(ship, T("mbPrice", "Price"), s.Price, 0, 999999999, v => { s.Price = v; MarkDirty(); });
            Int(ship, T("mbEquipmentSlots", "Equipment slots"), s.SlotCount(3), 0, 40, v => { s.SetSlotCount(3, v); MarkDirty(); BuildLoadoutPage(); });
            if (original != null)
                Note(ship, string.Format(T("mbOriginalStats", "Original: {0} hull, {1} t, handling {2}, {3}."), original.armor, original.cargo,
                                         Mathf.RoundToInt(original.handling), ItemInfo.Credits(original.price)));
            BuildShipMore(s);
            BuildShipIcon(s);
            if (s.IsKitShip) { BuildKitShipSection(s); return; }   // its look is the kit's parts, its height over the pad measured

            Heading(ship, T("mbModel", "Model"));
            bool gameModel = s.HasGameModel;
            bool hasModel = !string.IsNullOrEmpty(s.Model) && !gameModel;
            int modelShip = gameModel && GameModels.Is(s.Model, out string modelName) ? GameModels.ShipOf(modelName) : -1;
            Note(ship, gameModel ? string.Format(T("mbGameModelNote", "The game's {0} model, as it is: it sits on the pads like that ship."),
                                                 modelShip >= 0 ? GameNames.Ship(modelShip) : s.Model)
                       : hasModel ? s.Model : T("mbOriginalModel", "The game's own model."));
            var mrow = Add(ship, new VisualElement(), "mbv-row");
            if (ModImport.Available)
                Act(mrow, hasModel ? T("mbReplaceModel", "Replace model…") : T("mbUseModel", "Use a .glb model…"), () => PickModel(s));
            Act(mrow, T("mbGameModel", "Game model…"), () => PickGameShip(T("mbGameModelTitle", "Use a game ship's model"), i => UseGameShipModel(s, i)));
            if (hasModel) Act(mrow, T("mbOptimizeModel", "Optimize…"), () => OpenOptimize(s.Model.Replace('\\', '/')));
            // A new ship on a game model: textures of its own in the Skins kind (textures/ships/<id>/; the game's ship keeps its look).
            if (ModProject.OwnsSkinOf(s) && ModBuilder.IndexOf(s) is int ownIndex && ownIndex >= 0)
                Act(mrow, T("mbOwnTextures", "Its own textures…"), () =>
                {
                    Project.AddSkin(ownIndex);
                    SaveNow();
                    ModBuilder.SkinShip = ownIndex;
                    SelectKind(ModBuilder.Kind.Skins);
                });
            if (s.IsOverride && (hasModel || gameModel)) Act(mrow, T("mbOriginalModelButton", "Original model"), () =>
            {
                s.Model = null;
                if (gameModel)
                {
                    // Back to the ship's own model: its own hardpoints too.
                    s.Mounts.Clear();
                    s.Mounts.AddRange(Database.GameMounts(s.OverrideIndex).Select(ProjectShip.Copy));
                }
                RequestReload(true);
                BuildShipPage();
            });
            if (hasModel)
            {
                Int(ship, T("mbLength", "Length nose to tail (units, 20 = 1 m)"), Mathf.RoundToInt(s.ModelLength), 50, 200000,
                    v => ResizeModel(s, v));
                var srow = Add(ship, new VisualElement(), "mbv-row");
                Add(srow, new Label(T("mbScale", "Scale")), "mbv-inline-label");
                Act(srow, "−10%", () => ResizeModel(s, Mathf.Max(50f, Mathf.Round(s.ModelLength / 1.1f))));
                Act(srow, "+10%", () => ResizeModel(s, Mathf.Min(200000f, Mathf.Round(s.ModelLength * 1.1f))));
                // The model's orientation in its file: a turn about each axis in 90 deg steps (Unity Euler x / y / z).
                void TurnRow(string key, string english, Func<float> get, Action<float> set)
                {
                    var row = Add(ship, new VisualElement(), "mbv-row");
                    Add(row, new Label($"{T(key, english)}: {Mathf.RoundToInt(get())}°"), "mbv-inline-label");
                    Act(row, "−90°", () => TurnModel(s, () => set(Mathf.Repeat(get() - 90f, 360f))));
                    Act(row, "+90°", () => TurnModel(s, () => set(Mathf.Repeat(get() + 90f, 360f))));
                }
                TurnRow("mbYawAxis", "Turn (yaw)", () => s.ModelYaw, v => s.ModelYaw = v);
                TurnRow("mbPitchAxis", "Tip (pitch)", () => s.ModelPitch, v => s.ModelPitch = v);
                TurnRow("mbRollAxis", "Roll", () => s.ModelRoll, v => s.ModelRoll = v);
                Note(ship, T("mbYawNote", "Turn the model until its nose follows the floor arrow and its top faces up."));
                // The model's place on the ship's pivot (game units; the hardpoints stay where they are).
                Add(ship, new Label(T("mbOffset", "Position (units from the pivot)").ToUpperInvariant()), "mbv-label");
                var off = s.ModelOffset;
                var orow = Add(ship, new VisualElement(), "mbv-row", "mbv-xyz");
                Int(orow, "X", off.x, -200000, 200000, v => NudgeModel(s, 0, v - s.ModelOffset.x));
                Int(orow, "Y", off.y, -200000, 200000, v => NudgeModel(s, 1, v - s.ModelOffset.y));
                Int(orow, "Z", off.z, -200000, 200000, v => NudgeModel(s, 2, v - s.ModelOffset.z));
                var nrow = Add(ship, new VisualElement(), "mbv-row");
                int step = Mathf.Max(5, Mathf.RoundToInt(s.ModelLength * 0.02f));   // 2 % of the length per press
                for (int axis = 0; axis < 3; axis++)
                {
                    int a = axis;
                    Act(nrow, "XYZ"[a] + "−", () => NudgeModel(s, a, -step), "mbv-step");
                    Act(nrow, "XYZ"[a] + "+", () => NudgeModel(s, a, step), "mbv-step");
                }
                Act(nrow, T("mbCentreModel", "Centre on pivot"), () => CentreModel(s));
                BuildTextures(s);
                BuildAddParts(s);
            }
            if (!s.IsOverride && !gameModel)
            {
                var hrow = Add(ship, new VisualElement(), "mbv-row");
                Int(hrow, T("mbHangarHeight", "Height over the pad"), s.HangarHeight, 0, 20000, v => { s.HangarHeight = v; RequestReload(true); });
                Act(hrow, T("mbAuto", "Auto"), () => { AutoHangarHeight(); BuildShipPage(); });
            }
        }
        /// <summary>Hull textures for a model whose look comes as separate PNGs (one material for the whole hull); a model
        /// with its own textures needs none.</summary>
        void BuildTextures(ProjectShip s)
        {
            Heading(ship, T("mbTextures", "Textures"));
            if (!s.HullTexturesEditable) { Note(ship, T("mbTexturesManual", "This ship's materials are set by hand in ships.json.")); return; }
            Note(ship, T("mbTexturesNote", "Only for a model without textures (it shows white): maps for the whole hull."));
            foreach (var (role, english) in TextureRoles)
            {
                string r = role;
                string current = s.HullTexture(role);
                var row = Add(ship, new VisualElement(), "mbv-row");
                Add(row, new Label($"{T("mbTex_" + role, english)}: {(current != null ? System.IO.Path.GetFileName(current) : T("mbNone", "none"))}"), "mbv-inline-label");
                Act(row, T("mbChoose", "Choose…"), () => PickImage(T("mbPickTexture", "Choose a texture"), path =>
                    {
                        string id = s.IsOverride ? "ship_" + s.OverrideIndex : s.LocalId;
                        s.SetHullTexture(r, Project.AddTexture(path, $"{id}_{r}"));
                        RequestReload(false);
                        BuildShipPage();
                    }));
                if (current != null) Act(row, "×", () => { s.SetHullTexture(r, null); RequestReload(false); BuildShipPage(); }, "mbv-step");
            }
        }
        /// <summary>The ship's glowing parts (ships.json "addParts", CustomAddPart): the original's additive layers beside the
        /// hull, e.g. an original-game mod's lights and engine glows, made in the hull model's own frame and drawn with the
        /// game's material for their role. Listed with a texture (its model's own, else the game's glow; a chosen PNG
        /// replaces either) and a remove button; a new one picked as a .glb / .aem for each role.</summary>
        void BuildAddParts(ProjectShip s)
        {
            Heading(ship, T("mbAddParts", "Glowing parts"));
            Note(ship, T("mbAddPartsNote", "Lights and engine glows modelled beside the hull (in the same place and size), drawn like the game's own ships draw theirs."));
            var parts = s.AddParts;
            for (int i = 0; i < parts.Count; i++)
            {
                int index = i;
                var (model, role, texture) = parts[i];
                var row = Add(ship, new VisualElement(), "mbv-row");
                string text = $"{PartRoleName(role)}: {System.IO.Path.GetFileName(model ?? "")}";
                if (!string.IsNullOrEmpty(texture)) text += $" ({System.IO.Path.GetFileName(texture)})";
                Add(row, new Label(text), "mbv-inline-label");
                // A texture for the part (a player's report: a custom engine glow showed the game's glow sprite).
                Act(row, T("mbPartTexture", "Texture…"), () => PickImage(T("mbPickTexture", "Choose a texture"), path =>
                    {
                        string id = s.IsOverride ? "ship_" + s.OverrideIndex : s.LocalId;
                        s.SetPartTexture(index, Project.AddTexture(path, $"{id}_{role}"));
                        RequestReload(false);
                        BuildShipPage();
                    }));
                if (!string.IsNullOrEmpty(texture)) Act(row, T("mbPartTextureOff", "No texture"), () => { s.SetPartTexture(index, null); RequestReload(false); BuildShipPage(); });
                Act(row, "×", () => { s.RemovePart(index); RequestReload(false); BuildShipPage(); }, "mbv-step");
            }
            if (!ModImport.Available) return;
            var arow = Add(ship, new VisualElement(), "mbv-row");
            foreach (var role in CustomAddPart.Roles)
            {
                string r = role;
                Act(arow, "+ " + PartRoleName(r), () => PickGlb(T("mbPickPart", "Choose the part's model"), path =>
                {
                    string id = s.IsOverride ? "ship_" + s.OverrideIndex : s.LocalId;
                    s.AddPart(r, Project.AddModel(path, $"{id}_{r}"));
                    RequestReload(false);
                    BuildShipPage();
                    Status(T("mbLoadingModel", "Loading the model…"), false);
                }));
            }
        }
        /// <summary>The model was turned: rebuilt, and its height over the pad measured again once it is (its bottom moved).</summary>
        void OnModelTurned() => OnModelMoved();
        /// <summary>The model turned by 'apply' (its pitch / yaw / roll) about its own centre, at the size it has: the ship
        /// builder sizes the model by its extent along the nose (modelLength) and turns it about its file's origin, so a
        /// model standing up shrank as it was turned nose forward, and one modelled far from its origin swung out of the
        /// hangar (a player's video, 2026-10-25). The length nose to tail and the offset follow the turn instead (the
        /// hardpoints stay, as before); measured on the hull as built, else a plain turn.</summary>
        void TurnModel(ProjectShip s, Action apply)
        {
            var before = Quaternion.Euler(s.ModelPitch, s.ModelYaw, s.ModelRoll);
            apply();
            var hull = boundTransform != null ? boundTransform.Find("hull") : null;
            var renderers = hull != null ? hull.GetComponentsInChildren<Renderer>(true) : null;
            var pts = new List<Vector3>();
            if (renderers != null)
                foreach (var r in renderers)
                {
                    var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null || r.name.Contains("_add") || r.name.Contains("glow")) continue;
                    var mb = mesh.bounds;
                    var m = hull.worldToLocalMatrix * r.transform.localToWorldMatrix;   // the hull's own frame: its turn and scale out
                    for (int i = 0; i < 8; i++)
                        pts.Add(Vector3.Scale(hull.localScale, m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))));
                }
            if (pts.Count == 0) { OnModelTurned(); return; }
            Bounds Box(Quaternion q)
            {
                var b = new Bounds(q * pts[0], Vector3.zero);
                foreach (var p in pts) b.Encapsulate(q * p);
                return b;
            }
            var after = Quaternion.Euler(s.ModelPitch, s.ModelYaw, s.ModelRoll);
            Bounds was = Box(before), now = Box(after);
            if (was.size.z > 1e-4f && now.size.z > 1e-4f)
            {
                float k = s.ModelLength * M / was.size.z;   // the project's size (the hull may still be an older build)
                s.ModelLength = Mathf.Clamp(s.ModelLength * now.size.z / was.size.z, 50f, 200000f);   // not rounded: turned back, the same size
                var du = (was.center - now.center) * k;   // Unity metres: the centre back where it was
                s.ModelOffset += new Vector3Int(Mathf.RoundToInt(-du.x / M), Mathf.RoundToInt(du.y / M), Mathf.RoundToInt(du.z / M));
            }
            OnModelTurned();
        }
        /// <summary>The model was moved or resized: rebuilt, its height over the pad measured again.</summary>
        void OnModelMoved()
        {
            autoHeightAfterReload = Current != null && !Current.IsOverride;
            RequestReload(true);
            BuildShipPage();
        }
        /// <summary>The model moved 'units' along an axis; its hardpoints go with it.</summary>
        void NudgeModel(ProjectShip s, int axis, int units)
        {
            if (units == 0) return;
            var d = Vector3Int.zero;
            d[axis] = units;
            MoveModel(s, d);
        }
        void MoveModel(ProjectShip s, Vector3Int delta)
        {
            s.ModelOffset += delta;
            foreach (var m in s.Mounts)
            {
                if (m.position_engine == null || m.position_engine.Length < 3) continue;
                for (int i = 0; i < 3; i++) m.position_engine[i] += delta[i];
            }
            gizmo.Refresh();
            OnModelMoved();
            BuildHardpointsPage();
        }
        /// <summary>The model sized to 'length' (nose to tail); its hardpoints scale with it about the model's origin.</summary>
        void ResizeModel(ProjectShip s, float length)
        {
            float k = length / Mathf.Max(1f, s.ModelLength);
            var o = s.ModelOffset;
            s.ModelLength = length;
            foreach (var m in s.Mounts)
            {
                if (m.position_engine == null || m.position_engine.Length < 3) continue;
                for (int i = 0; i < 3; i++) m.position_engine[i] = o[i] + Mathf.RoundToInt((m.position_engine[i] - o[i]) * k);
                if (m.glowSize != null) for (int i = 0; i < m.glowSize.Length; i++) m.glowSize[i] = Mathf.Round(m.glowSize[i] * k);
            }
            gizmo.Refresh();
            OnModelMoved();
            BuildHardpointsPage();
        }
        /// <summary>The hull's box centred on the ship's pivot (from the model as it is built now).</summary>
        void CentreModel(ProjectShip s)
        {
            if (boundTransform == null) return;
            var c = gizmo.LocalBounds.center;   // Unity ship frame, with the current offset in it
            MoveModel(s, new Vector3Int(Mathf.RoundToInt(c.x / M), -Mathf.RoundToInt(c.y / M), -Mathf.RoundToInt(c.z / M)));
        }
        // Hardpoints ------------------------------------------------------------------------------------------------

        void BuildHardpointsPage()
        {
            hardpoints.Clear();
            var s = Current;
            if (s == null) return;
            Heading(hardpoints, T("mbAddPoint", "Add a hardpoint"));
            var addRow = Add(hardpoints, new VisualElement(), "mbv-row", "mbv-add-row");
            for (int t = 0; t < 4; t++)
            {
                int type = t;
                var b = Act(addRow, "+ " + T("mbType" + TypeNames[t], TypeNames[t]), () => { placing = placing == type ? -1 : type; BuildHardpointsPage(); }, "mbv-type-button");
                b.style.borderLeftColor = ModBuilderGizmo.TypeColours[t];
                b.EnableInClassList("mbv-type-button--on", placing == type);
            }
            Note(hardpoints, T("mbPointsHelp", "Drag a point or its arrows (Ctrl snaps). Drag elsewhere to turn, wheel to zoom. Delete removes, M mirrors."));

            for (int t = 0; t < 3; t++)
            {
                int type = t;
                var row = Add(hardpoints, new VisualElement(), "mbv-row", "mbv-slot-row");
                Add(row, new Label(string.Format(T("mbSlotsLine", "{0} slots: {1}  ·  {2} points"), T("mbType" + TypeNames[t], TypeNames[t]), s.SlotCount(t), s.MountCount(t))), "mbv-inline-label");
                Act(row, "−", () => { s.SetSlotCount(type, s.SlotCount(type) - 1); MarkDirty(); BuildHardpointsPage(); BuildLoadoutPage(); }, "mbv-step");
                Act(row, "+", () => { s.SetSlotCount(type, s.SlotCount(type) + 1); MarkDirty(); BuildHardpointsPage(); BuildLoadoutPage(); }, "mbv-step");
            }

            Heading(hardpoints, T("mbPoints", "Hardpoints"));
            var counts = new int[4];
            for (int i = 0; i < s.Mounts.Count; i++)
            {
                int index = i;
                var m = s.Mounts[i];
                int type = Mathf.Clamp(m.slotType, 0, 3);
                counts[type]++;
                var p = m.position_engine ?? new int[3];
                var row = new Button(() => { sound(false); gizmo.Selected = index; gizmo.Refresh(); BuildHardpointsPage(); }) { focusable = false };
                row.AddToClassList("mbv-point-row");
                row.EnableInClassList("mbv-point-row--on", index == gizmo.Selected);
                var dot = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-dot");
                dot.style.backgroundColor = ModBuilderGizmo.TypeColours[type];
                Add(row, new Label($"{T("mbType" + TypeNames[type], TypeNames[type])} {counts[type]}") { pickingMode = PickingMode.Ignore }, "mbv-point-name");
                Add(row, new Label($"{p[0]}, {p[1]}, {p[2]}") { pickingMode = PickingMode.Ignore }, "mbv-point-pos");
                hardpoints.Add(row);
            }
            if (s.Mounts.Count == 0) Note(hardpoints, T("mbNoPoints", "No hardpoints: pick a kind above, then click on the hull."));
            if (s.IsKitShip) Note(hardpoints, T("mbKitShipPoints", "A kit ship's hardpoints come from its parts; these only cover kinds no part has."));
            BuildSelectedPoint();
        }
        void BuildSelectedPoint(VisualElement into = null)
        {
            var s = EditedMounts;
            int i = gizmo.Selected;
            if (s == null || i < 0 || i >= s.Count) { px = null; return; }
            var m = s[i];
            m.position_engine ??= new int[3];
            int type = Mathf.Clamp(m.slotType, 0, 3);
            var selectedBox = Add(into ?? hardpoints, new VisualElement(), "mbv-selected");
            Heading(selectedBox, string.Format(T("mbSelectedPoint", "Selected: {0}"), T("mbType" + TypeNames[type], TypeNames[type])));
            var xyz = Add(selectedBox, new VisualElement(), "mbv-row", "mbv-xyz");
            px = Int(xyz, "X", m.position_engine[0], -200000, 200000, v => { m.position_engine[0] = v; OnMountEdited(type); });
            py = Int(xyz, "Y", m.position_engine[1], -200000, 200000, v => { m.position_engine[1] = v; OnMountEdited(type); });
            pz = Int(xyz, "Z", m.position_engine[2], -200000, 200000, v => { m.position_engine[2] = v; OnMountEdited(type); });
            Note(selectedBox, T("mbXyzNote", "Game units from the centre: X left, Y up, Z forward (20 = 1 m)."));
            var acts = Add(selectedBox, new VisualElement(), "mbv-row");
            Act(acts, T("mbMirror", "Mirror"), Mirror);
            Act(acts, T("mbCentre", "Centre X"), () => { m.position_engine[0] = 0; OnMountEdited(type); RefreshPoints(); });
            Act(acts, T("mbDelete", "Delete"), DeleteSelected, "mbv-danger");
            if (type == 2)
            {
                // The way the turret's base faces (WeaponMount.turretUp): a preset, or the hull's surface at the point.
                Add(selectedBox, new Label(T("mbTurretFaces", "Turret faces")), "mbv-inline-label");
                var faces = Add(selectedBox, new VisualElement(), "mbv-row", "mbv-add-row");
                var up = m.TurretUpLocal;
                foreach (var (key, label, dir) in TurretFacings)
                {
                    var d = dir;
                    var b = Act(faces, T(key, label), () => { SetTurretUp(m, d); OnMountEdited(2); RefreshPoints(); }, "mbv-type-button");
                    b.EnableInClassList("mbv-type-button--on", Vector3.Dot(up, new Vector3(-d.x, d.y, d.z)) > 0.999f);
                }
                Act(selectedBox, T("mbTurretMatchHull", "Match the hull here"), () =>
                {
                    if (gizmo.HullNormalAt(i, out var n)) { m.SetTurretUp(gizmo.GameDirOf(n), true); OnMountEdited(2); RefreshPoints(); }
                    else Status(T("mbTurretNoHull", "No hull found at that point."), true);
                });
                var g = new Vector3(-up.x, up.y, up.z);
                Note(selectedBox, string.Format(T("mbTurretFacesNote", "Facing {0:0.##}, {1:0.##}, {2:0.##}. Dragging it over the hull turns it to the surface."), g.x, g.y, g.z));
            }
            if (type == 3)
            {
                // The flame's box (WeaponMount.glowSize: half width, half height, length) and its outline in it (glowShape).
                float Size(int k) => m.glowSize != null && m.glowSize.Length > k ? m.glowSize[k]
                                   : k == 0 ? 24f : k == 1 ? Size(0) : 0.87f * 0.5f * (Size(0) + Size(1));
                void SetSize(int k, float v)
                {
                    var g = new[] { Size(0), Size(1), Size(2) };
                    g[k] = v;
                    m.glowSize = g;
                    OnMountEdited(3);
                }
                float particles = m.turretAngles != null && m.turretAngles.Length > 0 ? m.turretAngles[0] : 0.55f;
                Add(selectedBox, new Label(T("mbFlameShape", "Flame shape")), "mbv-inline-label");
                var shapes = Add(selectedBox, new VisualElement(), "mbv-row", "mbv-add-row");
                string shape = EngineGlowShape.Normalize(m.glowShape);
                foreach (var name in EngineGlowShape.Names)
                {
                    string n = name;
                    var b = Act(shapes, T("mbShape_" + n, char.ToUpperInvariant(n[0]) + n.Substring(1)), () =>
                    {
                        m.glowShape = n == "round" ? null : n;
                        OnMountEdited(3);
                        RefreshPoints();
                    }, "mbv-type-button");
                    b.EnableInClassList("mbv-type-button--on", n == shape);
                }
                Int(selectedBox, T("mbFlameWidth", "Flame half width (units)"), Mathf.RoundToInt(Size(0)), 1, 5000, v => SetSize(0, v));
                Int(selectedBox, T("mbFlameHeight", "Flame half height (units)"), Mathf.RoundToInt(Size(1)), 1, 5000, v => SetSize(1, v));
                Int(selectedBox, T("mbFlameLength", "Flame length (units)"), Mathf.RoundToInt(Size(2)), 0, 10000, v => SetSize(2, v));
                Note(selectedBox, T("mbFlameSizeNote", "Width and height from the centre to the edge; the length pulls the flame's flared ring back from the point."));
                Int(selectedBox, T("mbParticles", "Exhaust particles (%)"), Mathf.RoundToInt(particles * 100f), 0, 1000, v =>
                {
                    float f = v / 100f;
                    m.turretAngles = new[] { f, f, f };
                    MarkDirty();
                });
                if (kind == ModBuilder.Kind.Ships && Current.IsOverride && string.IsNullOrEmpty(Current.Model))
                    Note(selectedBox, T("mbEngineOriginal", "With the game's own model the flame is part of it: only the particles move."));
            }
        }
        void RefreshSelectedFields()
        {
            var s = EditedMounts;
            int i = gizmo.Selected;
            if (s == null || i < 0 || i >= s.Count || px == null) return;
            var p = s[i].position_engine;
            px.SetValueWithoutNotify(p[0]);
            py.SetValueWithoutNotify(p[1]);
            pz.SetValueWithoutNotify(p[2]);
        }
        // Loadout ---------------------------------------------------------------------------------------------------

        void BuildLoadoutPage()
        {
            loadout.Clear();
            var s = Current;
            if (s == null) return;
            var db = Database.Shared;
            Heading(loadout, T("mbTestFlight", "Test flight"));
            var flags = Add(loadout, new VisualElement(), "mbv-row");
            var god = Act(flags, ModBuilder.TestGodMode ? T("mbGodOn", "God mode: on") : T("mbGodOff", "God mode: off"),
                          () => { ModBuilder.TestGodMode = !ModBuilder.TestGodMode; MarkDirty(); BuildLoadoutPage(); });
            god.EnableInClassList("mbv-toggle--on", ModBuilder.TestGodMode);
            var ammo = Act(flags, ModBuilder.TestInfiniteAmmo ? T("mbAmmoOn", "Infinite ammo: on") : T("mbAmmoOff", "Infinite ammo: off"),
                           () => { ModBuilder.TestInfiniteAmmo = !ModBuilder.TestInfiniteAmmo; MarkDirty(); BuildLoadoutPage(); });
            ammo.EnableInClassList("mbv-toggle--on", ModBuilder.TestInfiniteAmmo);
            Act(loadout, T("mbAutoFill", "Auto-fill every slot"), () =>
            {
                Project.SetLoadout(s, DefaultLoadout(db, s));
                MarkDirty();
                ModBuilder.ApplyShip();
                BuildLoadoutPage();
            });
            var slots = SlotsOf(db, s);
            for (int kind = 0; kind < 4; kind++)
            {
                if (s.SlotCount(kind) <= 0) continue;
                Heading(loadout, T("mbKind" + kind, kind == 0 ? "Primary weapons" : kind == 1 ? "Secondary weapons" : kind == 2 ? "Turrets" : "Equipment"));
                for (int i = 0; i < s.SlotCount(kind); i++)
                {
                    int k = kind, slot = i;
                    int item = slot < slots[k].Count ? slots[k][slot] : -1;
                    var row = new Button(() => { sound(false); PickItem(k, slot); }) { focusable = false };
                    row.AddToClassList("mbv-item-row");
                    var icon = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-item-icon");
                    if (item >= 0) { var tex = ItemInfo.ItemIcon(item); if (tex != null) icon.style.backgroundImage = new StyleBackground(tex); }
                    Add(row, new Label(item >= 0 ? GameNames.Item(item) : T("mbEmptySlot", "— empty —")) { pickingMode = PickingMode.Ignore }, "mbv-item-name");
                    loadout.Add(row);
                }
            }
        }
        /// <summary>The project's loadout of 's' sorted into its slots by kind (item numbers).</summary>
        List<int>[] SlotsOf(Database db, ProjectShip s)
        {
            var slots = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            foreach (var st in Project.Loadout(s) ?? new List<ItemStack>())
            {
                var it = db.Item(st.item);
                if (it == null || it.TypeId < 0 || it.TypeId > 3) continue;
                slots[it.TypeId].Add(st.item);
            }
            return slots;
        }
        void SetSlot(int kind, int slot, int item)
        {
            var db = Database.Shared;
            var s = Current;
            var slots = SlotsOf(db, s);
            while (slots[kind].Count <= slot) slots[kind].Add(-1);
            slots[kind][slot] = item;
            var list = new List<ItemStack>();
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < Mathf.Min(slots[k].Count, s.SlotCount(k)); i++)
                    if (slots[k][i] >= 0) list.Add(new ItemStack(slots[k][i], k == 1 ? ModBuilder.Ammo : 1));
            Project.SetLoadout(s, list);
            MarkDirty();
            ModBuilder.ApplyShip();
            BuildLoadoutPage();
        }
        /// <summary>ModBuilder.DefaultLoadout for the ship as it is being edited (its slot counts, which the game reads only
        /// after the project is saved and read again).</summary>
        static List<ItemStack> DefaultLoadout(Database db, ProjectShip s)
        {
            var l = new List<ItemStack>();
            ItemData Best(int kind, params string[] categories)
            {
                var items = ModBuilder.ItemsFor(db, kind);
                foreach (var c in categories)
                {
                    var b = items.Where(i => i.category == c).OrderByDescending(i => i.maxPrice).FirstOrDefault();
                    if (b != null) return b;
                }
                return items.OrderByDescending(i => i.maxPrice).FirstOrDefault();
            }
            var p = Best(0, "Laser", "Blaster");
            for (int i = 0; i < s.SlotCount(0) && p != null; i++) l.Add(new ItemStack(p.index, 1));
            var sec = Best(1, "Missile");
            for (int i = 0; i < s.SlotCount(1) && sec != null; i++) l.Add(new ItemStack(sec.index, ModBuilder.Ammo));
            var tur = Best(2, "Turret");
            for (int i = 0; i < s.SlotCount(2) && tur != null; i++) l.Add(new ItemStack(tur.index, 1));
            int eq = 0;
            foreach (var cat in new[] { "Shield", "Armor", "Booster", "Scanner", "Tractor beam", "Steering nozzle" })
            {
                if (eq >= s.SlotCount(3)) break;
                var e = ModBuilder.ItemsFor(db, 3).Where(i => i.category == cat).OrderByDescending(i => i.maxPrice).FirstOrDefault();
                if (e != null) { l.Add(new ItemStack(e.index, 1)); eq++; }
            }
            return l;
        }
        void OnMountEdited(int type)
        {
            gizmo.Refresh();
            MarkDirty();
            if (type == 3 && (kind == ModBuilder.Kind.Kits || !string.IsNullOrEmpty(Current?.Model))) RequestReload(true);   // the flame is built into the model
            if (type == 2) RequestReload(true);   // the turntable's turrets sit on the mounts as the game reads them
        }
        static void SetTurretUp(WeaponMount m, Vector3 game) => m.SetTurretUp(game);
        void OnMountMoved() => RefreshSelectedFields();
        /// <summary>A new point where the hull was clicked; a turret faces out of the hull there ('normal', world).</summary>
        void AddMount(int type, Vector3 world, Vector3 normal)
        {
            var s = EditedMounts;
            var g = gizmo.GameOf(world);
            var m = new WeaponMount { slotType = type, position_engine = new[] { g.x, g.y, g.z } };
            if (type == 2) m.SetTurretUp(gizmo.GameDirOf(normal), true);
            if (type == 3)
            {
                float r = Mathf.Clamp(Mathf.Round(Mathf.Min(gizmo.LocalBounds.size.x, gizmo.LocalBounds.size.y) / M * 0.05f), 8f, 200f);
                m.glowSize = new[] { r, r, r * 0.87f };
                m.turretAngles = new[] { 0.55f, 0.55f, 0.55f };
            }
            s.Add(m);
            if (type < 3 && SlotsFollow && Current.SlotCount(type) < Current.MountCount(type)) Current.SetSlotCount(type, Current.MountCount(type));
            gizmo.Selected = s.Count - 1;
            gizmo.Refresh();
            OnMountEdited(type);
            RefreshPoints();
            BuildLoadoutPage();
        }
        void DeleteSelected()
        {
            var s = EditedMounts;
            int i = gizmo.Selected;
            if (s == null || i < 0 || i >= s.Count) return;
            int type = s[i].slotType;
            int before = s.Count(x => x.slotType == type);
            s.RemoveAt(i);
            if (type < 3 && SlotsFollow && Current.SlotCount(type) == before) Current.SetSlotCount(type, before - 1);   // the slots followed the points
            gizmo.Selected = -1;
            gizmo.Refresh();
            OnMountEdited(type);
            RefreshPoints();
            BuildLoadoutPage();
        }
        /// <summary>A copy of the selected point on the other side (x mirrored); a point on the centre line has none.</summary>
        void Mirror()
        {
            var s = EditedMounts;
            int i = gizmo.Selected;
            if (s == null || i < 0 || i >= s.Count) return;
            var m = s[i];
            if (m.position_engine[0] == 0) { Status(T("mbMirrorCentre", "The point is on the centre line: move it sideways first."), true); return; }
            var c = ProjectShip.Copy(m);
            c.position_engine[0] = -c.position_engine[0];
            if (c.turretUp != null && c.turretUp.Length >= 3) c.turretUp[0] = -c.turretUp[0];   // a side turret faces the other side
            s.Insert(i + 1, c);
            if (c.slotType < 3 && SlotsFollow && Current.SlotCount(c.slotType) < Current.MountCount(c.slotType)) Current.SetSlotCount(c.slotType, Current.MountCount(c.slotType));
            gizmo.Selected = i + 1;
            gizmo.Refresh();
            OnMountEdited(c.slotType);
            RefreshPoints();
            BuildLoadoutPage();
        }
        /// <summary>The pivot's height over the pad from the hull's lowest point.</summary>
        void AutoHangarHeight()
        {
            var s = Current;
            if (s == null || s.IsOverride || s.HasGameModel || boundTransform == null) return;   // a game model sits like its ship
            s.HangarHeight = Mathf.Max(0, Mathf.CeilToInt(-gizmo.LocalBounds.min.y / M) + 4);
            RequestReload(true);
        }
        /// <summary>A new ship's first hardpoints from its model's box: two guns at the front, a secondary under it, an engine
        /// at the back; and its height over the pad.</summary>
        void AutoLayout(ProjectShip s)
        {
            s.NeedsLayout = false;
            var b = gizmo.LocalBounds;
            int G(float unity) => Mathf.RoundToInt(unity / M);
            // A model made far from its file's origin (a player's Aegir: ~89 000 units off) is centred on the pivot (as
            // Centre on pivot does), else it stood kilometres from the pad and the camera with it.
            if (!b.Contains(Vector3.zero))
            {
                s.ModelOffset += new Vector3Int(G(b.center.x), -G(b.center.y), -G(b.center.z));
                b = new Bounds(Vector3.zero, b.size);
            }
            int w = G(b.extents.x * 0.45f), cy = G(b.center.y), front = G(b.max.z - b.size.z * 0.2f), back = G(b.min.z), under = G(b.min.y + b.size.y * 0.3f);
            s.Mounts.Clear();
            s.Mounts.Add(new WeaponMount { slotType = 0, position_engine = new[] { -w, cy, front } });
            s.Mounts.Add(new WeaponMount { slotType = 0, position_engine = new[] { w, cy, front } });
            s.Mounts.Add(new WeaponMount { slotType = 1, position_engine = new[] { 0, under, G(b.center.z) } });
            float r = Mathf.Clamp(Mathf.Round(Mathf.Min(b.size.x, b.size.y) / M * 0.05f), 8f, 200f);
            s.Mounts.Add(new WeaponMount { slotType = 3, position_engine = new[] { 0, cy, back }, glowSize = new[] { r, r, r * 0.87f }, turretAngles = new[] { 0.55f, 0.55f, 0.55f } });
            s.SetSlotCount(0, 2);
            s.SetSlotCount(1, 1);
            s.HangarHeight = Mathf.Max(0, Mathf.CeilToInt(-b.min.y / M) + 4);
            Project.SetLoadout(s, DefaultLoadout(Database.Shared, s));
            RequestReload(false);
            RebuildPages();
            Status(T("mbLayoutDone", "Placed a first set of hardpoints; move them onto the hull."), false);
        }
    }
}
