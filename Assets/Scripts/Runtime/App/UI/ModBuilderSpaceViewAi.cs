// ModBuilderSpaceViewAi.cs
// The space Mod Builder screen as the MCP server's AI drives it (part of ModBuilderSpaceView; ModBuilderScreenAi has the
// hooks): a station picked (its orbit loads when it is another), a weapon picked for the firing range and the range firing
// or paused, the orbit camera from a named side of the station (world axes: the stations sit unturned) or reset to frame it.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.UI
{
    public partial class ModBuilderSpaceView
    {
        internal override string ExternalSelectEntry(string kindName, string id, JObject options)
        {
            if (Project == null) return "No project is open.";
            string r = id == null ? null : id.StartsWith("override:") || id.StartsWith("id:") ? id : "id:" + id;
            if (kindName == "stations")
            {
                var s = Project.Stations.FirstOrDefault(x => x.Ref == r);
                if (s == null) return $"No station \"{id}\" in the project.";
                PickStation(s);
                return null;
            }
            if (kindName == "npcShips")
            {
                var n = Project.NpcShips.FirstOrDefault(x => x.Ref == r);
                if (n == null) return $"No NPC ship \"{id}\" in the project.";
                currentNpc = n;
                RebuildNpcList();
                BuildNpcPage();
                ShowNpcPreview(true);
                return null;
            }
            if (kindName == "items" && kind == ModBuilder.Kind.WeaponFx)
            {
                var it = Project.Items.FirstOrDefault(x => x.Ref == r);
                if (it == null) return $"No item \"{id}\" in the project.";
                currentFx = it;
                currentEffect = null;
                RebuildFx();
                BuildFxPage();
                RestartRange();
                return null;
            }
            if (kindName == "weaponEffects" && kind == ModBuilder.Kind.WeaponFx)
            {
                var e = Project.FindWeaponEffect(id);
                if (e == null) return $"No weapon effect \"{id}\" in the project.";
                currentEffect = e;
                currentFx = null;
                RebuildFx();
                BuildFxPage();
                RestartRange();
                return null;
            }
            return base.ExternalSelectEntry(kindName, id, options);
        }

        internal override JObject ExternalSelection()
        {
            var o = base.ExternalSelection();
            o["entry"] = kind == ModBuilder.Kind.WeaponFx ? (currentEffect != null ? "effect:" + currentEffect.Id : currentFx?.Ref)
                       : kind == ModBuilder.Kind.NpcShips ? currentNpc?.Ref : currentStation?.Ref;
            o["orbit"] = Session.StationIndex;
            if (kind == ModBuilder.Kind.WeaponFx) o["rangeFiring"] = !rangePaused;
            return o;
        }

        // The AI's view, applied again after the screen frames the station or the range anew (a restart, a reload).
        Vector3 aiFrom;
        float aiZoom;
        bool aiView;

        internal override string ExternalCamera(string angle, float zoom, bool reset)
        {
            if (reset)
            {
                aiView = false;
                if (kind == ModBuilder.Kind.WeaponFx) RestartRange(); else if (kind == ModBuilder.Kind.NpcShips) FrameNpc(); else FrameStation();
                return null;
            }
            var view = ModelViewer.Angles.FirstOrDefault(a => a.name == angle);
            if (view.name == null) return $"angle: one of {string.Join(", ", ModelViewer.Angles.Select(a => a.name))}.";
            aiFrom = view.from.normalized;
            aiZoom = zoom;
            aiView = true;
            ApplyAiView();
            return null;
        }

        /// <summary>The AI's view on the framing just made (FrameStation, the range's).</summary>
        void ApplyAiView()
        {
            if (!aiView) return;
            yaw = Mathf.Atan2(aiFrom.x, aiFrom.z);
            pitch = Mathf.Clamp(Mathf.Asin(aiFrom.y), -1.45f, 1.45f);
            if (aiZoom > 0f) distance = Mathf.Clamp(radius * aiZoom, radius * 0.3f, radius * 12f);
            pendingTurn = Vector2.zero;
        }

        internal override string ExternalRange(bool fire)
        {
            if (kind != ModBuilder.Kind.WeaponFx) return base.ExternalRange(fire);
            if (currentFx == null && currentEffect == null) return "Pick a weapon or an effect first (select_entry items or weaponEffects in the weapon fx kind).";
            rangePaused = !fire;
            BuildFxPage();
            if (fire) RestartRange();
            return null;
        }
    }
}
