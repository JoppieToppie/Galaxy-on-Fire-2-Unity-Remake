// MenuBackground.cs
// The main menu backdrop, like the original (Reference/research/mainmenu_notes.md; ModMainMenu::OnInitialize 0x1a46dc,
// CutScene(2), Level(2)):
//   ModMainMenu::OnInitialize   Status::resetGame, AERandom::reset, Status::setStation(Galaxy::getStation(nextInt(100))):
//                               a random main-game station 0..99 on every menu entry (the ending's backdrop keeps the
//                               station and the game: Session.EndingPending)
//   Level::init (type 2)        a normal orbit (OrbitBuilder: sky, sun / planets, lights, fog, station, jumpgate,
//                               asteroids, dust) with the ordinary free-flight traffic (Level::createMission 0xbda70 on
//                               the empty mission: Traffic / TrafficPlan / NpcShip): patrolling local fighters, raiders
//                               fighting them, freighters crossing 1-4 km beside the station, the static Terran
//                               battleship, jumpers and respawns (Level::updateOrbit); the fighters turn away from the
//                               station's volumes (PlayerFighter::update, Obstacle) like in flight
//   the player                  exists but inactive (Player::setActive(false)) at the origin: the NPCs never target it
//   CutScene::process mode 2    the camera: a fixed spot, a slow yaw pan (MenuCamera)
// Remake-only: the station keeps full detail at every distance (this build of the game always draws LOD 0 anyway), the
// camera backs out of a big station's hull, no asteroid right at the camera, and the traffic's own music stays off under
// the menu theme.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    public class MenuBackground : MonoBehaviour
    {
        /// <summary>Unused (the old curated backdrops); kept so the menu scene's serialized data stays valid.</summary>
        [System.Serializable]
        public class Setup
        {
            public string label;
            public int station;
            public float cameraStartAngle = -1f;
        }

        [HideInInspector] public Setup[] setups;
        public MenuCamera menuCamera;
        public Light sunLight;
        public Light planetLight;

        [Tooltip("-1 = a random station 0..99 like the original (Galaxy::getStation(nextInt(100))).")]
        public int forceStation = -1;

        [Tooltip("Asteroids are kept this far (metres) from the camera, from their surface (remake: nothing fills the whole view).")]
        public float cameraKeepOut = 150f;

        public GameObject Station { get; private set; }
        public OrbitLayout Layout { get; private set; }
        public Traffic Traffic { get; private set; }

        void OnDestroy() => GoF2Remake.Flight.HullCollision.Suppressed = false;

        void Awake()
        {
            if ((GoF2Remake.World.SpaceHooks.ServerShutOff != null && GoF2Remake.World.SpaceHooks.ServerShutOff(gameObject.scene))) return;   // a dedicated server has no backdrop
            // Remake: no collision hulls for the backdrop's station and traffic (nobody flies here; cooking them only cost
            // loading), until the menu scene goes (OnDestroy).
            GoF2Remake.Flight.HullCollision.Suppressed = true;
            var db = Database.Load();
            bool ending = Session.EndingPending;
            if (!ending) Session.ResetNewGame();   // Status::resetGame
            int station = forceStation >= 0 ? forceStation : ending ? Session.StationIndex : Random.Range(0, 100);
            Layout = OrbitLayout.Build(db, station);
            OrbitScale.Use(Layout);   // Remake, experimental: the station scale (Options > Gameplay)
            Debug.Log($"MenuBackground: station {station} ({db.Stations.Find(s => s.index == station)?.name}), system {Layout.systemIndex}");

            OrbitBuilder.SetupSky(Layout);
            OrbitBuilder.SetupLights(Layout, sunLight, planetLight);

            Station = OrbitBuilder.SpawnStation(db, Layout, transform);
            if (Station != null) { GoF2Remake.Visuals.AutoLods.FullDetail(Station); foreach (var lg in Station.GetComponentsInChildren<LODGroup>()) lg.enabled = false; }
            var gate = OrbitBuilder.SpawnJumpgate(db, Layout, transform);
            OrbitBuilder.AddObstacles(Layout, Station, gate);
            SpawnStatics();

            // CutScene::initialize mode 2: (rnd(20000) - 20000, 0, rnd(60000) + 40000). Remake: the big stations reach
            // kilometres out in front (Tornard 57: 3.4 km), so a spot inside or against the hull backs straight out until
            // it is clear by a third of the station's size (at least 800 m).
            var camGame = new Vector3(Random.Range(0, 20000) - 20000f, 0f, Random.Range(0, 60000) + 40000f);
            if (Station != null)
            {
                var b = StationBounds(Station);
                float clear = Mathf.Max(800f, Mathf.Max(b.size.x, b.size.y, b.size.z) / 3f);
                b.Expand(clear * 2f);
                var at = OrbitScale.ToUnity(camGame);   // OrbitScale: the spot in the scaled orbit
                if (b.Contains(at)) camGame = OrbitScale.ToGame(new Vector3(at.x, at.y, b.min.z - 1f));   // Unity -z = game +z
            }
            if (menuCamera != null)
            {
                // OrbitScale: the far plane reaches past the grown station and the orbit pushed out with it.
                if (Layout.Scaled) menuCamera.far += 3f * (Layout.PlaceRadius(Layout.stationReach * 2f) - Layout.stationReach * 2f);
                menuCamera.Place(camGame);
            }
            var camPos = OrbitScale.ToUnity(camGame);
            // Measured from the asteroid's surface: the biggest (scale 2.2) reach 390 m from their centre (Void crystals 670 m),
            // so a centre 150 m out still put the camera inside a rock.
            float keepOut = cameraKeepOut + Layout.AsteroidMeshRadius * 2.2f * OrbitLayout.MetersPerUnit;
            OrbitBuilder.SpawnAsteroids(db, Layout, transform, p => (p - camPos).sqrMagnitude < keepOut * keepOut);

            // A capital ship's point is rolled again while its hull would reach the camera (TrafficPlan.AtMenuCamera).
            TrafficPlan.MenuCamera = camGame;
            try { SpawnTraffic(db); }
            finally { TrafficPlan.MenuCamera = null; }
            OrbitBuilder.SpawnDust(Layout, transform);
            var cam = menuCamera != null ? menuCamera.GetComponent<Camera>() : Camera.main;
            OrbitBuilder.SpawnBackdrop(Layout, cam, transform);
            SkyLayers.Spawn(Layout, cam, transform);
        }

        static Bounds StationBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 100f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Level::createScene mode 2 at campaign mission 0x2b (the ending's backdrop): meshes 0x37d0 (beer) and 0x37d1
        /// (bra). The original leaves them at the origin, inside the station and too small to see; remake pick: they drift
        /// across the camera's view (EndingDrift), the bra half a crossing after the beer.</summary>
        void SpawnStatics()
        {
            if (Session.CampaignMission != 0x2b || Session.FreePlay) return;
            var story = StoryAssets.Load();
            if (story == null || story.menuStatics == null) return;
            var cam = menuCamera != null ? menuCamera.transform : Camera.main != null ? Camera.main.transform : null;
            float delay = 6f;
            foreach (var prefab in story.menuStatics)
            {
                if (prefab == null) continue;
                var go = Instantiate(prefab, Vector3.zero, OrbitLayout.RotationToUnity(Vector3.zero), transform);
                go.name = prefab.name;
                if (cam != null) go.AddComponent<EndingDrift>().Setup(cam, delay);
                delay += 11f;
            }
        }

        /// <summary>Level::createMission on the empty mission, as in flight (TrafficPlan), with the inactive player at the
        /// origin (Level::createPlayer, Player::setActive(false)): alive for nothing, so no NPC ever targets it.</summary>
        void SpawnTraffic(Database db)
        {
            NpcTables.InCampaignLevel = false;   // per-level flags the flight level sets
            NpcTables.LevelFreelanceType = -1;
            var player = new GameObject("Player (inactive)");
            player.transform.SetParent(transform, false);
            var target = player.AddComponent<Target>();
            target.isPlayer = true;
            target.untargetable = true;
            target.hp = 0f;
            Traffic = new GameObject("Traffic").AddComponent<Traffic>();
            Traffic.transform.SetParent(transform, false);
            Traffic.MenuBackdrop = true;
            Traffic.Setup(db, Layout, target, Station);
        }
    }
}
