// ModBuilderViewKinds.cs
// The Mod Builder's hangar screen (part of ModBuilderView): what the kinds with a view of their own share with the screen's
// frame: the picks remembered over an undo, the hint line, the per-frame work, Esc, the pointer over the 3D view (the rooms'
// free camera, the star map) and the clean-up. Each kind's file implements its part (ModBuilderViewBlueprints, ...Campaign,
// ...Interiors, ...Quests, ...Audio, ...Systems, ...Races).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        /// <summary>The kinds' picks before an undo; the returned action picks them again.</summary>
        Action RememberPicks()
        {
            string bp = currentBlueprint?.Ref, option = currentOption?.Ref, room = currentInterior?.Ref, quest = currentQuest?.Name, system = currentSystem?.Ref,
                   modRace = currentModRace?.Ref;
            return () =>
            {
                currentBlueprint = Project.Blueprints.Find(b => b.Ref == bp) ?? (Project.Blueprints.Count > 0 ? Project.Blueprints[0] : null);
                currentOption = Project.GameOptions.Find(o => o.Ref == option);
                currentInterior = Project.Interiors.Find(r => r.Ref == room) ?? (Project.Interiors.Count > 0 ? Project.Interiors[0] : null);
                currentQuest = Project.Quests.Find(q => q.Name == quest) ?? (Project.Quests.Count > 0 ? Project.Quests[0] : null);
                currentSystem = Project.Systems.Find(s => s.Ref == system) ?? (Project.Systems.Count > 0 ? Project.Systems[0] : null);
                currentModRace = Project.Races.Find(r => r.Ref == modRace) ?? Project.Races.FirstOrDefault();
                currentStep = null;
            };
        }

        /// <summary>The hint line at the foot for the kind (a marker being placed, a system being moved).</summary>
        string PlaceHintOfKind() => kind switch
        {
            ModBuilder.Kind.Interiors => InteriorHint(),
            ModBuilder.Kind.Systems => SystemsHint(),
            ModBuilder.Kind.Races => ModRacesHint(),
            _ => "",
        };

        void UpdateKind(float dt)
        {
            if (kind == ModBuilder.Kind.Interiors) UpdateInteriorView(dt);
            UpdateAudio();   // also the quests' Sound picker's preview
            UpdateScripts();
        }

        bool EscapeKind() => kind switch
        {
            ModBuilder.Kind.Interiors => EscapeInterior(),
            ModBuilder.Kind.Systems or ModBuilder.Kind.Races => EscapeSystems(),
            _ => false,
        };

        bool KindPointerDown(PointerDownEvent e) => kind == ModBuilder.Kind.Interiors && InteriorPointerDown(e);
        bool KindPointerMove(PointerMoveEvent e) => kind == ModBuilder.Kind.Interiors && InteriorPointerMove(e);
        bool KindPointerUp(PointerUpEvent e) => kind == ModBuilder.Kind.Interiors && InteriorPointerUp(e);

        void DisposeKinds()
        {
            EndInteriorView();
            CloseMap();
            StopAudio();
            DisposeScripts();
        }

        // ---- kinds of content ---------------------------------------------------------------------------------
        /// <summary>The station shows its hangar (the turntable ship, its orbit camera); the lounge kinds show the bar.</summary>
        bool InHangar => level.View == StationView.Hangar;
        /// <summary>The kind shows the hangar's turntable ship (and flies it).</summary>
        static bool ShipKind(ModBuilder.Kind k) => k == ModBuilder.Kind.Ships || k == ModBuilder.Kind.Items || k == ModBuilder.Kind.Skins
                                                 || k == ModBuilder.Kind.Blueprints || k == ModBuilder.Kind.Campaigns || k == ModBuilder.Kind.Kits;
        /// <summary>The kind edits hardpoints on the turntable ship: a ship's own, or a kit part's (ModBuilderViewKits).</summary>
        bool EditsMounts => kind == ModBuilder.Kind.Ships || kind == ModBuilder.Kind.Kits;
        /// <summary>The hardpoints the gizmo edits: the selected kit part's in the Kits kind, else the current ship's.</summary>
        List<WeaponMount> EditedMounts => kind == ModBuilder.Kind.Kits ? currentPart?.Mounts : Current?.Mounts;
        /// <summary>A ship's slot counts follow its hardpoints (a kit part's are its own "slots", set on its page).</summary>
        bool SlotsFollow => kind == ModBuilder.Kind.Ships && Current != null;
        /// <summary>The page listing the hardpoints shown again.</summary>
        void RefreshPoints()
        {
            if (kind == ModBuilder.Kind.Kits) BuildKitPage();
            else BuildHardpointsPage();
        }
        protected override bool EditsHere(ModBuilder.Kind k) => ModBuilder.PlaceOf(k) != ModBuilder.Place.Space;
        /// <summary>Shows a kind's panels; the hardpoint gizmo only with the ships; the lounge kinds in the Space Lounge, the
        /// systems on the star map, a room in itself.</summary>
        protected override void ShowKind(ModBuilder.Kind k, ModBuilder.Kind was)
        {
            bool ships = k == ModBuilder.Kind.Ships;
            placing = -1;
            if (gizmo.Selected >= 0) { gizmo.Selected = -1; gizmo.Refresh(); }
            gizmo.gameObject.SetActive(ships || k == ModBuilder.Kind.Kits);
            var place = ModBuilder.PlaceOf(k);
            bool bar = place == ModBuilder.Place.Lounge;
            if (place != ModBuilder.Place.Map && k != ModBuilder.Kind.AiTools) level.SetView(bar ? StationView.Lounge : StationView.Hangar);
            viewport.pickingMode = place == ModBuilder.Place.Map ? PickingMode.Ignore : PickingMode.Position;   // the map takes the pointer
            root.EnableInClassList("mbv-root--map", place == ModBuilder.Place.Map);
            drag = Drag.None;
            fling = Vector2.zero;
            // Test fly only where flying tests what is edited: a ship, an item fitted to it, a skin. Blueprints and the
            // campaign keep their ship on the turntable (and its engines) as a preview only.
            bool flies = Flies(k), turntable = ShipKind(k);
            testFly.style.display = flies ? StyleKeyword.Null : DisplayStyle.None;
            root.EnableInClassList("mbv-root--bar", !flies);   // no Test fly: the right panel reaches the bottom
            // The script editor takes the 3D view's place: its panel as wide as the screen allows.
            root.EnableInClassList("mbv-root--scripts", k == ModBuilder.Kind.Scripts);
            if (was == ModBuilder.Kind.Scripts && k != ModBuilder.Kind.Scripts) SaveScript();
            if (k == ModBuilder.Kind.Scripts) ShowScripts();
            uiEngines.style.display = turntable ? StyleKeyword.Null : DisplayStyle.None;
            // Item changes reach the rest of the builder (the loadout's pickers) once the game has read them.
            if (was == ModBuilder.Kind.Items && itemsChanged) { itemsChanged = false; RequestReload(true); }
            if (OnMap(was) && !OnMap(k)) CloseMap();   // Systems and Races share the map
            if (was == ModBuilder.Kind.Interiors && k != ModBuilder.Kind.Interiors) EndInteriorView();
            if (k == ModBuilder.Kind.Items) { RebuildItems(); BuildItemPage(); }
            if (k == ModBuilder.Kind.Characters) { RebuildCharacters(); BuildCharacterPage(); }
            if (k == ModBuilder.Kind.Skins) { RebuildSkins(); BuildSkinPage(); }
            if (k == ModBuilder.Kind.Blueprints) { RebuildBlueprints(); BuildBlueprintPage(); }
            if (k == ModBuilder.Kind.Kits) EnterKits();
            else if (was == ModBuilder.Kind.Kits) LeaveKits();
            if (k == ModBuilder.Kind.Campaigns) { RebuildCampaign(); BuildCampaignPage(); }
            if (k == ModBuilder.Kind.Interiors) { RebuildInteriors(); BuildInteriorPage(); ShowInteriorInScene(); }
            if (k == ModBuilder.Kind.Quests) { RebuildQuests(); BuildQuestPage(); }
            else UpdateQuestGraph();
            if (k == ModBuilder.Kind.Audio) { RebuildAudio(); BuildAudioPage(); }
            if (k == ModBuilder.Kind.Systems) { RebuildSystems(); BuildSystemPage(); OpenMap(); }
            if (k == ModBuilder.Kind.Races) { RebuildModRaces(); BuildModRacePage(); OpenMap(); }
            // The turntable swaps between the project's ship, the skinned game ship and the campaign's start ship.
            if (SwapsTurntable(was) != SwapsTurntable(k) && (int)was >= 0) { ModBuilder.ApplyShip(); boundShip = -2; }
            if (ships) { RebuildShips(); BuildHardpointsPage(); BuildLoadoutPage(); }
        }
        /// <summary>The kinds Test fly is offered in.</summary>
        static bool Flies(ModBuilder.Kind k) => k == ModBuilder.Kind.Ships || k == ModBuilder.Kind.Items || k == ModBuilder.Kind.Skins || k == ModBuilder.Kind.Kits;
        static bool SwapsTurntable(ModBuilder.Kind k) => k == ModBuilder.Kind.Skins || k == ModBuilder.Kind.Campaigns || k == ModBuilder.Kind.Blueprints;
        protected override bool CanReload => drag != Drag.Gizmo;
        protected override bool Dragging => drag != Drag.None;
        protected override void OnReloadStarted() => ModBuilder.ApplyShip();
        protected override void OnReloaded()
        {
            ModBuilder.ApplyShip();
            boundShip = -2;
            RebuildShips();
            if (kind == ModBuilder.Kind.Items) { RebuildItems(); BuildItemPage(); }
            if (kind == ModBuilder.Kind.Skins) { RebuildSkins(); BuildSkinPage(); }
            if (kind == ModBuilder.Kind.Blueprints) { RebuildBlueprints(); BuildBlueprintPage(); }
            if (kind == ModBuilder.Kind.Kits) { RebuildKits(); BuildKitPage(); }
            if (kind == ModBuilder.Kind.Campaigns) { RebuildCampaign(); BuildCampaignPage(); }
            if (kind == ModBuilder.Kind.Quests) { RebuildQuests(); BuildQuestPage(); }
            if (kind == ModBuilder.Kind.Audio) { RebuildAudio(); BuildAudioPage(); }
            // (an undo may have changed the files; the editor's own unsaved typing stays)
            if (kind == ModBuilder.Kind.Scripts && !scriptDirty) ShowScripts();
            if (kind == ModBuilder.Kind.Characters) { RebuildCharacters(); BuildCharacterPage(); }
            if (kind == ModBuilder.Kind.Systems) { RebuildSystems(); BuildSystemPage(); RefreshMap(); }
            if (kind == ModBuilder.Kind.Races) { RebuildModRaces(); BuildModRacePage(); RefreshMap(); }
            if (kind == ModBuilder.Kind.Interiors) { RebuildInteriors(); BuildInteriorPage(); }
        }
        protected override Action BeforeUndo()
        {
            string shipRef = Current?.Ref, itemRef = currentItem?.Ref, characterRef = currentCharacter?.Ref, kitId = currentKit?.Id, partId = currentPart?.Id;
            var picks = RememberPicks();
            return () =>
            {
                ModBuilder.Ship = Project.Ships.FirstOrDefault(s => s.Ref == shipRef) ?? Project.Ships.FirstOrDefault();
                currentItem = Project.Items.FirstOrDefault(i => i.Ref == itemRef) ?? Project.Items.FirstOrDefault();
                currentCharacter = Project.Characters.FirstOrDefault(c => c.Ref == characterRef) ?? Project.Characters.FirstOrDefault();
                currentKit = Project.FindKit(kitId) ?? Project.Kits.FirstOrDefault();
                currentPart = currentKit?.Find(partId) ?? currentKit?.Parts.FirstOrDefault();
                picks?.Invoke();
                var skins = Project.SkinShips();
                if (!skins.Contains(ModBuilder.SkinShip)) ModBuilder.SkinShip = skins.Count > 0 ? skins[0] : -1;
                gizmo.Selected = -1;
                placing = -1;
                ModBuilder.ApplyShip();
                boundShip = -2;
                RebuildShips();
                RebuildPages();
                OnReloaded();
                gizmo.Refresh();
            };
        }
    }
}
