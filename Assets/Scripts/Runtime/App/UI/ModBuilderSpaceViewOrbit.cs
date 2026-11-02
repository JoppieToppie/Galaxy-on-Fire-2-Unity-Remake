// ModBuilderSpaceViewOrbit.cs
// ModBuilderSpaceView: the Stations kind's handles in the shown orbit (part of the partial ModBuilderSpaceView; its fields
// are in ModBuilderSpaceView.cs). Remake (a modder's suggestion: "move a station's jumpgate within its orbit"): the orbit
// layout's places of the station being edited ("orbit" in stations.json: the jumpgate, the arrivals, the docking pads)
// as World.ModBuilderGizmo balls bound to the station itself, whose frame makes a ball's game units the orbit's (the
// station turns 180 deg like a ship's model is mirrored: WeaponSystem.MountToLocal then the station's rotation is
// OrbitLayout.ToUnity). A ball drags over the station's hull (a pad) or along its arrows (Ctrl: 100-unit snap); let go,
// the place is saved and the orbit built again. Only while the orbit shown is the station's own (Show its orbit).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.UI
{
    public partial class ModBuilderSpaceView
    {
        ModBuilderGizmo orbitGizmo;
        readonly List<WeaponMount> orbitMounts = new List<WeaponMount>();
        /// <summary>What each ball is: "jumpgatePosition", "arrival", or a pad's index.</summary>
        readonly List<string> orbitKeys = new List<string>();
        bool orbitDragging;
        string orbitBound;

        /// <summary>The station page's orbit places as balls, when its orbit is the one shown (null: none).</summary>
        JObject ShownOrbit() =>
            kind == ModBuilder.Kind.Stations && currentStation != null && level.Station != null && StationNumberOf(currentStation) == Session.StationIndex
                ? Obj(currentStation, "orbit") : null;

        static int[] Units(JToken t) => t is JArray a && a.Count >= 3 ? new[] { (int)Mathf.Round((float)a[0]), (int)Mathf.Round((float)a[1]), (int)Mathf.Round((float)a[2]) } : null;

        /// <summary>Per frame in the Stations kind: the balls follow the page (bound again when its places changed); hover.</summary>
        void UpdateOrbitGizmo()
        {
            var o = ShownOrbit();
            if (o == null) { HideOrbitGizmo(); return; }
            if (orbitDragging) return;
            string now = currentStation.Ref + "|" + o.ToString(Newtonsoft.Json.Formatting.None);
            if (orbitGizmo == null || now != orbitBound)
            {
                if (orbitGizmo == null) orbitGizmo = new GameObject("Orbit gizmo").AddComponent<ModBuilderGizmo>();
                orbitGizmo.cam = level.mainCamera;
                orbitMounts.Clear();
                orbitKeys.Clear();
                // jumpgate cyan (turret), arrival amber (secondary), pads red (primary)
                if (Units(o["jumpgatePosition"]) is int[] gate) { orbitMounts.Add(new WeaponMount { slotType = 2, position_engine = gate }); orbitKeys.Add("jumpgatePosition"); }
                if (Units(o["arrival"]) is int[] arrival) { orbitMounts.Add(new WeaponMount { slotType = 1, position_engine = arrival }); orbitKeys.Add("arrival"); }
                if (o["pads"] is JArray pads)
                    for (int i = 0; i < pads.Count; i++)
                        if (pads[i] is JObject pad && Units(pad["position"]) is int[] at) { orbitMounts.Add(new WeaponMount { slotType = 0, position_engine = at }); orbitKeys.Add(i.ToString()); }
                orbitGizmo.Bind(orbitMounts.Count > 0 ? level.Station.transform : null, orbitMounts);
                orbitBound = now;
            }
            if (Pointer.current != null) orbitGizmo.Hover(ScreenPos());
        }

        void HideOrbitGizmo()
        {
            if (orbitGizmo != null) UnityEngine.Object.Destroy(orbitGizmo.gameObject);
            orbitGizmo = null;
            orbitBound = null;
            orbitDragging = false;
        }

        /// <summary>A press on the view in the Stations kind: a ball or its arrows; false = the camera's (orbit).</summary>
        bool OrbitPointerDown(int button)
        {
            if (button != 0 || orbitGizmo == null || orbitGizmo.Ship == null) return false;
            if (!orbitGizmo.BeginDrag(ScreenPos())) return false;
            orbitDragging = true;
            return true;
        }

        void OrbitPointerMove()
        {
            if (!orbitDragging || orbitGizmo == null) return;
            var kb = Keyboard.current;
            orbitGizmo.Drag(ScreenPos(), kb != null && kb.ctrlKey.isPressed, 100);
        }

        /// <summary>Let go: the moved place into the station's "orbit" and the orbit built again.</summary>
        void OrbitPointerUp()
        {
            if (!orbitDragging) return;
            orbitDragging = false;
            orbitGizmo?.EndDrag();
            int i = orbitGizmo != null ? orbitGizmo.Selected : -1;
            var s = currentStation;
            if (s == null || i < 0 || i >= orbitMounts.Count) return;
            var o = Obj(s, "orbit");
            var p = orbitMounts[i].position_engine;
            var place = new JArray(p[0], p[1], p[2]);
            string key = orbitKeys[i];
            if (int.TryParse(key, out int pad))
            {
                if (!(o["pads"] is JArray pads) || !(pads.ElementAtOrDefault(pad) is JObject padObj)) return;
                // the approach point moves along with the pad
                if (Units(padObj["position"]) is int[] old && Units(padObj["approach"]) is int[] ap)
                    padObj["approach"] = new JArray(ap[0] + p[0] - old[0], ap[1] + p[1] - old[1], ap[2] + p[2] - old[2]);
                padObj["position"] = place;
            }
            else o[key] = place;
            Put(s, "orbit", o);
            Edited(true);
        }
    }
}
