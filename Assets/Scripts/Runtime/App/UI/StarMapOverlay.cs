// StarMapOverlay.cs
// StarMap: the overlay (part of the partial StarMap; its fields are in StarMap.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class StarMap
    {
        Item MakeItem(bool planet)
        {
            var it = new Item { root = new VisualElement { pickingMode = PickingMode.Ignore } };
            it.root.AddToClassList("map-item");
            // Modified: unseen until first placed (Place): for a frame every name box sat at the top left corner.
            it.root.style.visibility = Visibility.Hidden;
            it.pulse = Img(it.root, "map-pulse", Tex("map_pulse"));
            it.ring = Img(it.root, "map-ring", Tex("map_ring"));
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("map-name-row");
            var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
            wrap.AddToClassList("map-name-wrap");
            it.name = new Label { pickingMode = PickingMode.Ignore };
            it.name.AddToClassList("map-name");
            it.name.AddToClassList("gof-semibold");
            wrap.Add(it.name);
            if (!planet) { it.raceIcon = Img(wrap, "map-race-icon", null); }
            row.Add(wrap);
            it.root.Add(row);
            it.line1 = Line(it.root);
            it.line2 = Line(it.root);
            it.visited = Img(it.root, "map-icon", Tex("map_visited"));
            it.visited.style.left = 71.5f - 7f - 18f;
            it.visited.style.top = -71.5f + 10f - 35f + 143f;
            it.story = Img(it.root, "map-icon", Tex("map_story"));
            it.freelance = Img(it.root, "map-icon", Tex("map_freelance"));
            foreach (var icon in new[] { it.story, it.freelance })
            {
                icon.style.left = -71.5f - 7f + (icon == it.freelance ? -22f : 0f);
                icon.style.top = -71.5f + 10f;
                icon.style.display = DisplayStyle.None;
            }
            if (planet)
            {
                it.gate = Img(it.root, "map-icon", Tex("gate_icon"));
                it.gate.style.left = 71.5f - 7f;
                it.gate.style.top = -71.5f + 10f;
            }
            // Icon slot 4, 0x452 "Products finished" (StarMap::drawOnScreenInfo): a pending blueprint product waits there;
            // in the right column, below the gate icon when there is one (BuildPlanetItems).
            it.products = Img(it.root, "map-icon", Tex("map_products"));
            it.products.style.left = 71.5f - 7f;
            it.products.style.top = -71.5f + 10f;
            it.products.style.display = DisplayStyle.None;
            labels.Add(it.root);
            return it;
        }
        static VisualElement Img(VisualElement parent, string cls, Texture2D tex)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            if (tex != null)
            {
                e.style.backgroundImage = new StyleBackground(tex);
                if (cls == "map-icon") { e.style.width = tex.width; e.style.height = tex.height; }
            }
            parent.Add(e);
            return e;
        }
        static Label Line(VisualElement parent)
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("map-line");
            parent.Add(l);
            return l;
        }
        void BuildSystemItems()
        {
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || suns[s.index] == null) continue;
                var it = MakeItem(false);
                it.name.text = ItemInfo.WithTag(s.name, GameNames.SystemTag(s.index));   // remake mods: the mod's tag, dim
                // Multiplayer: a faction holding a station here (NetFactions).
                string faction = GoF2Remake.Multiplayer.NetFactionsClient.SystemTag(db, s.index);
                if (faction != null) it.name.text += $" [{faction}]";
                it.name.text += SquadMark(s.index, -1);   // multiplayer: squadmates here
                it.name.text += MobileMark(s.index, -1);  // remake mods: a mobile station in this system now
                bool owned = GalaxyMap.HasOwner(s.index) && (s.raceId >= 0 && s.raceId <= 3 || Modding.ModRaces.Icon(s.raceId) != null);
                // Remake: the owner's emblem in the player's standing colours (allied / neutral / enemy), the old radar icon as fallback;
                // remake mods: a mod race's own emblem.
                if (owned) it.raceIcon.style.backgroundImage = new StyleBackground(Modding.ModRaces.Icon(s.raceId) ?? StatusWindow.StandingIcon(s.raceId) ?? Tex($"race_{s.raceId}"));
                it.raceIcon.style.display = owned ? DisplayStyle.Flex : DisplayStyle.None;
                if (s.index == KaamoClub.SystemIndex && KaamoClub.Owned)
                {
                    // 0x545, the orange house of the owned Kaamo Club, where an owner's race icon would be.
                    it.raceIcon.style.backgroundImage = new StyleBackground(Tex("map_home"));
                    it.raceIcon.AddToClassList("map-race-icon--home");
                    it.raceIcon.style.display = DisplayStyle.Flex;
                }
                else if (s.index == KaamoClub.SystemIndex)
                {
                    // Remake: the pirate skull (race_8) on the Kaamo Club's system until the club is bought.
                    it.raceIcon.style.backgroundImage = new StyleBackground(Tex("race_8"));
                    it.raceIcon.style.display = DisplayStyle.Flex;
                }
                bool fully = s.stations.Count > 0 && s.stations.TrueForAll(st => Session.VisitedStations.Contains(st));
                it.visited.style.display = fully ? DisplayStyle.Flex : DisplayStyle.None;
                it.pulse.style.display = s.index == currentSystem ? DisplayStyle.Flex : DisplayStyle.None;
                it.story.style.display = s.stations.Exists(StoryMarked) ? DisplayStyle.Flex : DisplayStyle.None;
                it.freelance.style.display = FreelanceSystem >= 0 && FreelanceSystem == s.index ? DisplayStyle.Flex : DisplayStyle.None;
                it.products.style.display = Session.PendingProducts.Exists(pp => s.stations.Contains(pp.station)) ? DisplayStyle.Flex : DisplayStyle.None;
                if (builder?.movable != null && builder.movable(s.index)) it.name.style.color = new Color(1f, 0.78f, 0.35f);   // the builder's own
                systemItems[s.index] = it;
            }
        }
        void BuildPlanetItems()
        {
            foreach (var p in planets)
            {
                var it = MakeItem(true);
                it.name.text = ItemInfo.WithTag(StationName(p.station, false), GameNames.StationTag(p.station));
                if (GoF2Remake.Multiplayer.NetFactionsClient.Owner(p.station, out string faction, out _)) it.name.text = $"[{faction}] {it.name.text}";
                it.name.text += SquadMark(-1, p.station);   // multiplayer: squadmates at this station
                it.name.text += MobileMark(-1, p.station);  // remake mods: a mobile station in this orbit now
                it.visited.style.display = Session.VisitedStations.Contains(p.station) ? DisplayStyle.Flex : DisplayStyle.None;
                it.gate.style.display = p.gate ? DisplayStyle.Flex : DisplayStyle.None;
                it.pulse.style.display = p.station == currentStation ? DisplayStyle.Flex : DisplayStyle.None;
                it.story.style.display = StoryMarked(p.station) ? DisplayStyle.Flex : DisplayStyle.None;
                it.freelance.style.display = p.station == FreelanceTarget ? DisplayStyle.Flex : DisplayStyle.None;
                it.products.style.display = Session.PendingProducts.Exists(pp => pp.station == p.station) ? DisplayStyle.Flex : DisplayStyle.None;
                if (p.gate) it.products.style.top = -71.5f + 10f + 26f;   // the slots stack downward in 26 px steps
                planetItems.Add(it);
            }
        }
        void BuildKey()
        {
            keyBox.Clear();
            // StarMap::drawKey rows (drawn bottom-up there): Story, Freelance, Jumpgate, Already visited, Products finished.
            foreach (var (img, text) in new[] { ("map_story", 555), ("map_freelance", 556), ("gate_icon", 547), ("map_visited", 401), ("map_products", 274) })
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("key-row");
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("key-icon-cell");
                Img(cell, "map-icon", Tex(img)).style.position = Position.Relative;
                row.Add(cell);
                var l = new Label(T(text)) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("key-label");
                row.Add(l);
                keyBox.Add(row);
            }
        }
        void ToggleKey()
        {
            keyShown = !keyShown;
            keyBox.EnableInClassList("key-box--shown", keyShown);
        }
        bool Project(Vector3 unityWorld, out Vector2 panel)
        {
            panel = Vector2.zero;
            if (cam == null || root?.panel == null) return false;
            var v = cam.WorldToViewportPoint(unityWorld);
            if (v.z <= 0f) return false;
            var size = PanelSize;   // the map camera always fills the screen
            panel = new Vector2(v.x * size.x, (1f - v.y) * size.y);
            return true;
        }
        Vector2 PanelSize => root != null ? root.layout.size : new Vector2(1920f, 1080f);
        static void Place(VisualElement e, Vector2 p)
        {
            e.style.left = p.x;
            e.style.top = p.y;
            e.style.visibility = StyleKeyword.Null;   // Modified: shown once placed (MakeItem)
        }
        void UpdateOverlay()
        {
            var size = PanelSize;
            float pulse = (Mathf.Sin(timeMs * 0.005f) + 1f) * 0.5f;
            float galaxyAlpha = fade / 255f, systemAlpha = 1f - galaxyAlpha;

            foreach (var kv in systemItems)
            {
                int i = kv.Key;
                var it = kv.Value;
                bool front = Project(suns[i].position, out var p);
                bool show = front && galaxyAlpha > 0.01f && p.x > -50f && p.y > -50f && p.x < size.x + 50f && p.y < size.y + 50f
                            && !(revealing && i == revealSystem);
                it.root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                Place(it.root, p);
                bool sel = i == selected, other = selected >= 0 && !sel;
                it.root.style.opacity = galaxyAlpha;
                float labelScale = Mathf.Lerp(1f, 0.75f, OverviewEase);
                it.root.style.scale = new Scale(new Vector3(labelScale, labelScale, 1f));
                it.ring.EnableInClassList("map-ring--selected", sel);
                it.ring.style.backgroundImage = new StyleBackground(Tex(sel ? "map_ring_selected" : "map_ring"));
                it.ring.style.opacity = other ? 64f / 255f : 1f;
                it.name.EnableInClassList("map-name--selected", sel);
                it.name.style.opacity = other ? 64f / 255f : 1f;
                if (it.raceIcon != null) it.raceIcon.style.opacity = other ? 64f / 255f : 1f;
                it.pulse.style.opacity = pulse;
                var sys = db.Systems.Find(s => s.index == i);
                bool owned = GalaxyMap.HasOwner(i) && (sys.raceId >= 0 && sys.raceId <= 3 || Modding.ModRaces.Is(sys.raceId));
                float y = 68.5f + 30f;
                if (sel && owned) { it.line1.text = GoF2Remake.Flight.Standing.RaceName(sys.raceId); it.line1.style.top = y; y += 30f; }
                it.line1.style.display = sel && owned ? DisplayStyle.Flex : DisplayStyle.None;
                it.line2.style.display = sel ? DisplayStyle.Flex : DisplayStyle.None;
                if (sel) SetSecurity(it.line2, GalaxyMap.SecurityOf(sys), y);
                if (sel) it.root.BringToFront();
            }

            for (int k = 0; k < planetItems.Count; k++)
            {
                var it = planetItems[k];
                bool front = planetObjects[k] != null && Project(planetObjects[k].position, out var p) && systemAlpha > 0.01f;
                it.root.style.display = front ? DisplayStyle.Flex : DisplayStyle.None;
                if (!front) continue;
                Project(planetObjects[k].position, out p);
                Place(it.root, p);
                bool sel = k == selectedPlanet, other = selectedPlanet >= 0 && !sel;
                it.root.style.opacity = systemAlpha;
                it.ring.EnableInClassList("map-ring--selected", sel);
                it.ring.style.backgroundImage = new StyleBackground(Tex(sel ? "map_ring_selected" : "map_ring"));
                it.ring.style.opacity = other ? 64f / 255f : 1f;
                it.name.EnableInClassList("map-name--selected", sel);
                it.name.style.opacity = other ? 64f / 255f : 1f;
                it.pulse.style.opacity = pulse;
                int tech = db.Stations.Find(s => s.index == planets[k].station)?.techLevel ?? 0;
                it.line1.style.display = sel && tech > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (sel && tech > 0) { it.line1.text = $"{T(133)}: {tech}"; it.line1.style.top = 68.5f + 30f; }
                // Remake mods: what the place is (stations.json "subtitle") under the tech level.
                string sub = sel ? Modding.ModWorld.Subtitle(planets[k].station) : null;
                it.line2.style.display = sub != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (sub != null) { it.line2.text = sub; it.line2.style.color = Color.white; it.line2.style.top = 68.5f + (tech > 0 ? 60f : 30f); }
                if (sel) it.root.BringToFront();
            }

            // System block (system view) and the energy line (with a drive, another system selected).
            bool inSystem = systemView || zoomDir != 0;
            UpdatePlanetLore(inSystem, systemAlpha);
            systemHeader.style.display = inSystem && zoomSystem >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            systemHeader.style.opacity = systemAlpha;
            int energySystem = inSystem ? zoomSystem : selected;
            bool energy = jumpDrive && energySystem >= 0 && energySystem != currentSystem && cells > 0;
            // Remake (jumpgate route planning): without a drive the line counts the gate jumps of the planned route.
            bool route = !energy && PlansRoute && routePath != null && energySystem == selected;
            energyLine.style.display = energy || route ? DisplayStyle.Flex : DisplayStyle.None;
            if (energy)
            {
                energyLine.text = $"{T(578)} {cells} / {cellsInCargo}";
                energyLine.EnableInClassList("energy-line--short", cellsInCargo < cells);
            }
            else if (route)
            {
                energyLine.text = safeRoutePath == null ? RouteJumpsText(routePath.Count - 1)
                    : $"{RouteJumpsText(routePath.Count - 1)}  ·  {RouteSafestText}: {JumpCountText(safeRoutePath.Count - 1)}";
                energyLine.EnableInClassList("energy-line--short", false);
            }
            touch.MarkDirtyRepaint();
        }
        /// <summary>The selected planet's lore panel (stations.json "description"), in the system view only.</summary>
        void UpdatePlanetLore(bool inSystem, float alpha)
        {
            if (planetLore == null) return;
            int st = inSystem && selectedPlanet >= 0 && selectedPlanet < planets.Count ? planets[selectedPlanet].station : -1;
            string lore = st >= 0 ? Modding.ModWorld.Description(st) : null;
            planetLore.style.display = lore != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (lore == null) { loreStation = int.MinValue; return; }
            planetLore.style.opacity = alpha;
            if (st == loreStation) return;
            loreStation = st;
            string name = ItemInfo.WithTag(db.Stations.Find(x => x.index == st)?.name ?? "", GameNames.StationTag(st));
            string sub = Modding.ModWorld.Subtitle(st);
            planetLore.Q<Label>(className: "planet-lore-title").text = string.IsNullOrEmpty(sub) ? name : $"{name} · {sub}";
            planetLore.Q<Label>(className: "planet-lore-text").text = lore;
        }
        static void SetSecurity(Label l, int level, float top)
        {
            level = Mathf.Clamp(level, 0, 3);
            l.text = T(402 + level);
            l.style.color = (Color)GalaxyMap.SecurityColours[level];
            l.style.top = top;
        }
        void FillSystemHeader(int system)
        {
            var sys = db.Systems.Find(s => s.index == system);
            if (sys == null) return;
            bool owned = GalaxyMap.HasOwner(system) && (sys.raceId >= 0 && sys.raceId <= 3 || Modding.ModRaces.Is(sys.raceId));
            var logo = root.Q("systemLogo");
            var tex = !owned ? null : Modding.ModRaces.Is(sys.raceId) ? Modding.ModRaces.Icon(sys.raceId) : Tex($"logo_{sys.raceId}");   // (a mod race: its emblem)
            logo.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (tex != null)
            {
                bool modRace = Modding.ModRaces.Is(sys.raceId);   // (a mod race's emblem at a logo's size, whatever its PNG's)
                logo.style.backgroundImage = new StyleBackground(tex);
                logo.style.width = modRace ? 120f : tex.width; logo.style.height = modRace ? 120f : tex.height;
            }
            root.Q<Label>("systemName").text = ItemInfo.WithTag(sys.name, GameNames.SystemTag(sys.index));
            var race = root.Q<Label>("systemRace");
            race.text = owned ? GoF2Remake.Flight.Standing.RaceName(sys.raceId) : "";
            race.style.display = owned ? DisplayStyle.Flex : DisplayStyle.None;
            SetSecurity(root.Q<Label>("systemSecurity"), GalaxyMap.SecurityOf(sys), 0f);
            root.Q<Label>("systemSecurity").style.top = StyleKeyword.Auto;
        }
        /// <summary>StarMap::draw lines: the route network (white, alpha 34) and the pulse toward the gate neighbours.</summary>
        void DrawLines(MeshGenerationContext ctx)
        {
            if (cam == null || fade <= 0f) return;
            var p2d = ctx.painter2D;
            p2d.lineWidth = 2f;
            p2d.lineCap = LineCap.Round;
            float a = Mathf.Min(fade, 34f) / 255f;
            p2d.strokeColor = new Color(1f, 1f, 1f, a);
            foreach (var s in db.Systems)
            {
                if (s.index >= suns.Length || suns[s.index] == null) continue;
                foreach (int to in s.jumpRoutesTo)
                {
                    if (to < 0 || to >= suns.Length || suns[to] == null) continue;
                    if (revealing && (to == revealSystem || s.index == revealSystem)) continue;
                    DrawSegment(p2d, suns[s.index].position, suns[to].position);
                }
            }
            float t = (101f - (timeMs / 10f) % 99f) / 100f;
            DrawMissionRoute(p2d, t);
            if (jumpDrive || current == null || suns[currentSystem] == null) return;
            p2d.strokeColor = new Color(1f, 1f, 1f, Mathf.Min(255f * t, fade) / 255f);
            foreach (int to in current.jumpRoutesTo)
            {
                if (to < 0 || to >= suns.Length || suns[to] == null) continue;
                var target = suns[to].position;
                DrawSegment(p2d, suns[currentSystem].position, target + t * (suns[currentSystem].position - target));
            }
        }
        /// <summary>StarMap::draw, mission mode: the route segment by segment on the pulse's cycle (+0xe8 advances when t wraps).
        /// Remake: also the planned gate route to the selection on the station's and the gate's maps (PlanRoute).</summary>
        void DrawMissionRoute(Painter2D p2d, float t)
        {
            if (routePath == null || routePath.Count < 2) return;
            if (t > lastRouteT) routeSegment = (routeSegment + 1) % (routePath.Count - 1);   // t wrapped: the next segment
            lastRouteT = t;
            float a = fade / 255f;
            p2d.lineWidth = 3f;
            // Remake: the safest route, where it differs, steady in the Secure colour under the fastest one.
            if (safeRoutePath != null)
            {
                p2d.strokeColor = new Color(0f, 237f / 255f, 0f, a * 0.8f);
                for (int i = 0; i + 1 < safeRoutePath.Count; i++)
                {
                    var from = suns[safeRoutePath[i]]; var to = suns[safeRoutePath[i + 1]];
                    if (from != null && to != null) DrawSegment(p2d, from.position, to.position);
                }
            }
            p2d.strokeColor = new Color(1f, 1f, 0f, a);
            for (int i = 0; i < routeSegment; i++)
            {
                var from = suns[routePath[i]]; var to = suns[routePath[i + 1]];
                if (from != null && to != null) DrawSegment(p2d, from.position, to.position);
            }
            var s0 = suns[routePath[routeSegment]]; var s1 = suns[routePath[routeSegment + 1]];
            if (s0 != null && s1 != null)
            {
                p2d.strokeColor = new Color(1f, 1f, 0f, Mathf.Min(t, a));
                DrawSegment(p2d, s0.position, s1.position + t * (s0.position - s1.position));
            }
            p2d.lineWidth = 2f;
        }
        void DrawSegment(Painter2D p2d, Vector3 a, Vector3 b)
        {
            bool fa = Project(a, out var pa), fb = Project(b, out var pb);
            if (!fa && !fb) return;
            if (!fa || !fb)
            {
                // Clip the end behind the camera to just in front of the near plane.
                var near = cam.transform.position + cam.transform.forward * (cam.nearClipPlane * 1.01f);
                var n = cam.transform.forward;
                Vector3 front = fa ? a : b, back = fa ? b : a;
                float da = Vector3.Dot(front - near, n), db2 = Vector3.Dot(back - near, n);
                var clipped = Vector3.Lerp(front, back, da / (da - db2));
                if (!Project(clipped, out var pc)) return;
                if (fa) pb = pc; else pa = pc;
            }
            p2d.BeginPath();
            p2d.MoveTo(pa);
            p2d.LineTo(pb);
            p2d.Stroke();
        }
        /// <summary>Multiplayer: the squadmates in 'system' (a count) or at 'station' (their names), green; red with "⚠" when
        /// one of them calls for help (NetDistress). "" = none (or no session). Read when the map opens.</summary>
        string SquadMark(int system, int station)
        {
            if (!GoF2Remake.Multiplayer.NetGame.Active) return "";
            var me = GoF2Remake.Multiplayer.NetPlayer.Local;
            int count = 0;
            bool distress = false;
            var names = new System.Text.StringBuilder();
            foreach (var m in GoF2Remake.Multiplayer.NetSquad.Members())
            {
                if (m == null || m == me || GoF2Remake.Multiplayer.NetArena.IsArenaOrbit(m.Station)) continue;
                var st = db.Stations.Find(x => x.index == m.Station);
                if (st == null || (station >= 0 ? st.index != station : st.system != system)) continue;
                count++;
                distress |= m.Distress;
                if (names.Length > 0) names.Append(", ");
                names.Append(m.Distress ? "⚠ " : "").Append(m.DisplayName);
            }
            if (count == 0) return "";
            string colour = distress ? "#ff6655" : "#78e68c";
            return station >= 0 ? $"\n<color={colour}>● {names}</color>" : $" <color={colour}>{(distress ? "⚠" : "●")}{count}</color>";
        }
        /// <summary>Remake mods: the mobile stations (Modding.MobileStations) in 'system' (their names) or in the orbit of
        /// 'station' (their names and how long until they leave its system), amber. "" = none. Read when the map opens.</summary>
        string MobileMark(int system, int station)
        {
            var mobiles = ModWorld.Mobiles();
            if (mobiles.Count == 0) return "";
            double now = MobileStations.Clock;
            var sb = new System.Text.StringBuilder();
            foreach (int mobile in mobiles)
            {
                int host = MobileStations.HostAt(mobile, now);
                if (host < 0 || (station >= 0 ? host != station : SystemOf(host) != system)) continue;
                string name = db.Stations.Find(x => x.index == mobile)?.name ?? "?";
                if (station < 0) { sb.Append($" <color=#ffb347>● {name}</color>"); continue; }
                int minutes = Mathf.Max(1, Mathf.CeilToInt((float)MobileStations.LeavesSystemIn(mobile, now) / 60f));
                sb.Append($"\n<color=#ffb347>● {name} · {Localization.Extra("mapMobileLeaves", "leaves in {0} min").Replace("{0}", minutes.ToString())}</color>");
            }
            return sb.ToString();
        }
    }
}
