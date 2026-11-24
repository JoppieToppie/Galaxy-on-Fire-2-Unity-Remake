// StationLevelInspect.cs
// StationLevel: remake: Inspect ship (HangarInspect) (part of the partial StationLevel; its fields are in StationLevel.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace GoF2Remake.World
{
    public partial class StationLevel
    {
        /// <summary>Starts inspecting the player's ship: only in the hangar, with the ship on its pad (not flying), not in VR.</summary>
        public bool BeginInspect() => BeginInspect(1.15f);
        /// <summary>Inspect ship's orbit, 'minRadii' ship radii at the closest (the Mod Builder's camera goes nearer).</summary>
        public bool BeginInspect(float minRadii)
        {
            if (View != StationView.Hangar || playerShip == null || PlayerFlying || GoF2Remake.Data.VrFlags.Enabled || mainCamera == null) return false;
            // The hull's box in the ship's own frame (a world box around a ship turned 45 deg is ~1.3x too big), without the
            // additive glow layers, unless there is nothing else.
            var local = ShipLocalBounds(playerShip, false);
            if (local.size == Vector3.zero) local = ShipLocalBounds(playerShip, true);
            if (local.size == Vector3.zero) local = new Bounds(Vector3.zero, Vector3.one * 20f);
            var scale = playerShip.lossyScale;
            var world = new Bounds(playerShip.TransformPoint(local.center), Vector3.Scale(local.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            flingVelocity = 0f;
            AddRoomColliders();
            AddShipColliders();
            Inspect.Blocked = RoomClearance;
            Inspect.Begin(mainCamera.transform.position, world, minRadii);   // its extents' length is the hull's radius
            return true;
        }
        public void EndInspect() => Inspect.End();
        /// <summary>Modified: the room's meshes as colliders on layer RoomColliderLayer for Inspect ship's camera (the hangar
        /// rooms are imported readable for it, AssetImport), once per room; not its additive / animated layers, bots or ships.
        /// Always its own colliders on child objects, on a layer of their own: the Mod Builder's interiors view puts
        /// MeshColliders on the same meshes (its RoomLayer 21) and destroys them when it ends, so skipping a mesh that had one
        /// left the room without walls; made again when any of them is gone.</summary>
        void AddRoomColliders()
        {
            if (HangarRoom == null) return;
            if (roomCollidersFor == HangarRoom && roomColliders.Count > 0 && roomColliders.TrueForAll(c => c != null)) return;
            foreach (var c in roomColliders) if (c != null) Destroy(c.gameObject);
            roomColliders.Clear();
            roomCollidersFor = HangarRoom;
            int unreadable = 0;
            foreach (var mf in HangarRoom.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                string n = mf.gameObject.name;
                if (mesh == null || n.Contains("_add") || n.Contains("_anim") || n.Contains("_bot_") || n.Contains("_lights")) continue;
                if (!mesh.isReadable) { unreadable++; continue; }
                var go = new GameObject("Inspect collider");
                go.layer = RoomColliderLayer;
                go.transform.SetParent(mf.transform, false);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                roomColliders.Add(mc);
            }
            // A Library imported before the hangars were made readable (ModelOrientationPostprocessor's version 2) has them
            // unreadable: the camera then goes through the walls. Said once.
            if (unreadable > 0 && !loggedUnreadableRoom)
            {
                loggedUnreadableRoom = true;
                Debug.LogWarning($"Inspect ship: {unreadable} mesh(es) of {HangarRoom.name} aren't readable, so the camera won't stop at them " +
                                 "(reimport Models/main/hangars: GoF2 > Import > Reimport Models Only).");
            }
        }
        /// <summary>Modified: the player's ship as colliders on ShipColliderLayer for Inspect ship's free camera (the ship models
        /// are imported readable, AssetImport.IsHullModel); not its glow, light or engine layers. Once per ship model.</summary>
        void AddShipColliders()
        {
            if (playerShip == null || shipCollidersFor == playerShip) return;
            shipCollidersFor = playerShip;
            foreach (var mf in playerShip.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                string n = mf.gameObject.name;
                if (mesh == null || !mesh.isReadable || n.Contains("_add") || n.Contains("_lights") || n.Contains("_engine") || n.Contains("_glow")) continue;
                var go = new GameObject("Inspect collider");
                go.layer = ShipColliderLayer;
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }
        /// <summary>Modified: how far a sphere of CameraRadius goes from 'from' along 'dir' (at most 'max') before 'mask''s
        /// colliders, and the surface's normal there. Where the sphere starts touching one (it would skip it), a ray along the
        /// move stands in too, stopped CameraRadius short.</summary>
        static float CameraCast(Vector3 from, Vector3 dir, float max, int mask, out Vector3 normal)
        {
            normal = -dir;
            float free = max;
            if (Physics.SphereCast(from, CameraRadius, dir, out var hit, max, mask, QueryTriggerInteraction.Ignore))
            {
                free = hit.distance;
                normal = hit.normal;
            }
            if (Physics.CheckSphere(from, CameraRadius, mask, QueryTriggerInteraction.Ignore)
                && Physics.Raycast(from, dir, out var ray, max + CameraRadius, mask, QueryTriggerInteraction.Ignore)
                && ray.distance - CameraRadius < free)
            {
                free = Mathf.Max(ray.distance - CameraRadius, 0f);
                normal = ray.normal;
            }
            return free;
        }
        /// <summary>Modified: Inspect ship's free camera moving from 'from' to 'to': stopped short of the room's walls and the
        /// hull (CameraCast along the move; it slides along a wall only by the parts of the move that stay clear).</summary>
        public Vector3 RoomMove(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return to;
            float free = CameraCast(from, d / len, len, FreeCameraMask, out var normal);
            if (free >= len) return to;
            // Slide: the part of the move along the wall, if that way is clear.
            var stop = from + d / len * Mathf.Max(free - 0.05f, 0f);
            var slide = Vector3.ProjectOnPlane(to - stop, normal);
            float slideLen = slide.magnitude;
            if (slideLen > 1e-3f && CameraCast(stop, slide / slideLen, slideLen, FreeCameraMask, out _) >= slideLen)
                return stop + slide;
            return stop;
        }
        /// <summary>Modified: how far from 'from' along 'dir' the orbit camera can go before the room: CameraCast, 0.5 m short
        /// of the sphere's contact, so the camera stops 0.9 m from the wall.</summary>
        float RoomClearance(Vector3 from, Vector3 dir, float max)
        {
            float free = CameraCast(from, dir, max, 1 << RoomColliderLayer, out _);
            return free >= max ? max : Mathf.Max(free - 0.5f, 0f);
        }
        /// <summary>The meshes' bounds in 'ship''s own space (the shown ones; 'withAdditive' takes the glow layers too).</summary>
        static Bounds ShipLocalBounds(Transform ship, bool withAdditive)
        {
            bool any = false;
            var b = new Bounds();
            var toShip = ship.worldToLocalMatrix;
            foreach (var mf in ship.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled || (!withAdditive && mf.name.Contains("_add"))) continue;
                var lb = mf.sharedMesh.bounds;
                var m = toShip * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }
        /// <summary>SpaceLounge::OnTouchEnd case 0 jumps to B; remake: the rest of the ease runs at a 0.7 s pace instead, so the
        /// camera doesn't snap.</summary>
        public void SkipIntro()
        {
            if (IntroPlaying) introSkip = true;
        }
        /// <summary>ModStation::leaveStation 0xec1ec after the "Depart the station?" confirmation: into space (after the
        /// remake's take-off).</summary>
        public void Launch()
        {
            // ModStation::OnTouchEnd, leaving at campaign 0x18 from Thynome (10): the AB-1 Retractor seller of index 23 has
            // moved on (Agent::setEvent(1) + setOfferAccepted: 858 "I don't have anything left, sorry."), as step 23's Info
            // page 192 warns.
            if (!Session.FreePlay && Story.Index == 24 && Layout.stationIndex == 10 && Stock?.agents != null)
                foreach (var a in Stock.agents)
                    if (a.offer == AgentOffer.SellItem && a.sellItem == 68) a.accepted = a.known = true;   // known: no first-chat offer
            Depart(LaunchNow);
        }
        /// <summary>Remake: the player's ship takes off and flies out through the forcefield, then 'then' runs (loads the
        /// next scene); straight away with the flights off. It lifts off at once and holds over its pad while another
        /// ship flies (HangarTraffic: one flight at a time).</summary>
        public void Depart(Action then)
        {
            if (PlayerFlying) return;
            var lane = Lane;
            if (!Settings.HangarFlights || lane == null || playerShip == null) { then?.Invoke(); return; }
            SetView(StationView.Hangar);
            // The camera eases back to the hangar's for the take-off first: the flight works out where the ship leaves the
            // view (HangarFlight's shrink) from the camera as it starts, and from the orbit's close-up (the Mod Builder's
            // camera) it shrank away before it reached the forcefield.
            if (Inspect.Shown)
            {
                Inspect.End();
                pendingDeparture = then;
                return;
            }
            flingVelocity = 0f;
            afterDeparture = then;
            var engine = HangarFlight.AddEngine(playerShip.gameObject, true, db, shipIndex, out float volume);
            playerFlight = HangarFlight.Departure(playerShip, lane, engine, volume, hangarView);
            playerFlightStart = Time.unscaledTime;
            playerFlight.Hold = traffic != null && traffic.Busy;
        }
        /// <summary>A tap / key during the player's flight: landed at once, or gone. Not in its first 0.4 s (the key that
        /// confirmed the launch).</summary>
        public void SkipPlayerFlight()
        {
            if (playerFlight != null && Time.unscaledTime - playerFlightStart > 0.4f) playerFlight.Skip();
        }
        void LaunchNow()
        {
            Session.LastDepartureTime = Time.realtimeSinceStartup;   // Status+0x70, for computerTradeGoods
            Session.ArrivedByTravel = false;
            Session.LaunchedFromStation = true;
            // Remake mods: EVERSPACE 2's take-off from the dock in the station's orbit instead of the launch camera (Es2Dock)
            Session.Es2TakeOff = Es2Docked;
            if (Es2Docked) Session.LaunchedFromStation = false;
            int storyOrbit = Story.LaunchStation;
            if (orbitDocked && storyOrbit < 0)
            {
                // docked inside the orbit: this scene goes, the ship takes off from its pad there
                Session.Es2TakeOff = false;
                orbit.UndockFromOrbit();
                return;
            }
            if (storyOrbit >= 0)
            {
                // Index 48: departStation(58) + initStreamOutPosition, arriving in B'akrram's orbit (Taret Orskk flies).
                Session.PreviousStationIndex = Session.StationIndex;
                Session.StationIndex = storyOrbit;
                Session.ArrivedByTravel = true;
                Session.LaunchedFromStation = false;
                Session.Es2TakeOff = false;
            }
            if (Application.CanStreamedLevelBeLoaded(spaceScene)) SceneManager.LoadScene(spaceScene);
        }
    }
}
