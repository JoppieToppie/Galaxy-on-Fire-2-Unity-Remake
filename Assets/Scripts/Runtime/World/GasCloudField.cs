// GasCloudField.cs
// The Supernova plasma clouds of an orbit (Reference/research/space_props.md 2, weapons_special.md 3.5 / 6.5, and the
// decompiled PlayerGasCloud 0x1a5330 / explode 0x1a55f0 / update 0x1a5a1c):
//   Level::createGasClouds 0xbd750   only with a spectral filter mounted (sort 33) and not in the alien orbit; the orbit's
//                                    plasma is the top of Galaxy::getPlasmaProbabilities (items 201-204 Green / Blue / Purple /
//                                    Red, p = 100 - the map distance to the plasma's cheapest system, 0 below 50, sorted,
//                                    minus 2 per rank); no clouds of Red outside Thynome's system (10) in a system without
//                                    gate routes; count trunc((rnd(4) + 4) * p / 100) (+3 at campaign 142 in Kernstal's
//                                    orbit, the first cloud at (92000, 0, 36000)); each anywhere in +-80000 but not within
//                                    25000 of the station nor 30000 of the asteroid field
//   PlayerGasCloud                   a camera-facing billboard ~7000 units across; an ionizing missile's blast (Gun sort 34)
//                                    within its reach bursts it once into N = ((1.5 - d/mag) * 130 / 1.5 + 10) * attr56 / 100
//                                    plasma sparks around the blast point (+-(1.5 - d/mag) * 5000), flying toward and through
//                                    the cloud's centre at 7 k u/ms slowing by 0.08 per ms to k (k = 3..6), living 8000-22000
//                                    ms, then shrinking out over 1500 ms
//   collecting                       with a plasma collector (sort 35) in the turret view: a spark in the turret's sight and
//                                    within attr 51 of the player flies to the turret at attr 49 u/ms (from 2000 ms after the
//                                    burst); within 800 units: +1 t of the plasma (the hold full: nothing), sound 2256; sparks
//                                    shrink toward the turret over the last 3500 units ((d - 800) / 2700)
// The sight test (Radar::draw's turret scope box, not traced) is the turret's aim within 8 degrees.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class GasCloudField : MonoBehaviour
    {
        const float M = 0.05f;
        const float CollectUnits = 800f, ShrinkUnits = 3500f, CollectDelayMs = 2000f, SightDegrees = 8f;

        class Spark
        {
            public Transform tr;
            public Vector3 dir;
            public float speed, minSpeed, lifeMs, scale = 1f;
            public bool Alive => lifeMs > -1500f;
        }

        class Cloud
        {
            public GameObject go;
            public Vector3 position;   // Unity
            public bool exploded, done;
            public float ageMs;
            public readonly List<Spark> sparks = new List<Spark>();
        }

        readonly List<Cloud> clouds = new List<Cloud>();
        GameObject cloudPrefab;
        Transform sparkTemplate;
        Vector3 sparkBaseScale = Vector3.one;
        Database db;
        ShipController ship;
        /// <summary>The collector the player looks through (remake: one of several turrets), refreshed once a frame (Update).</summary>
        PlayerTurret turret;

        /// <summary>The one in the turret view, else the first plasma collector, else the first turret; looked up on the player,
        /// so a hull swap's turrets are found.</summary>
        PlayerTurret FindTurret()
        {
            var all = PlayerTurret.On(ship != null ? ship.gameObject : null);
            foreach (var t in all) if (t.InTurretView) return t;
            foreach (var t in all) if (t.IsCollector) return t;
            return all.Count > 0 ? all[0] : null;
        }
        AudioSource sfx;
        AudioClip[] collected;

        /// <summary>The orbit's plasma item (201-204), -1 = no clouds.</summary>
        public int PlasmaItem { get; private set; } = -1;
        /// <summary>1 t of plasma collected (the item): PlayerGasCloud::update's red plasma hint.</summary>
        public event System.Action<int> PlasmaCollected;
        /// <summary>Campaign 142's script: any cloud burst into sparks ("ionized").</summary>
        public bool AnyExploded => clouds.Exists(c => c.exploded);
        public int Count => clouds.Count;
        public event System.Action<string> Message;

        static readonly string[] Assemblies = { "sn_gas_cloud_green_anim_lookat_add", "sn_gas_cloud_blue_anim_lookat_add",
                                                "sn_gas_cloud_violet_anim_lookat_add", "sn_gas_cloud_red_anim_lookat_add" };

        /// <summary>Level::createGasClouds: null without a spectral filter, in the alien orbit or with no clouds.</summary>
        public static GasCloudField Spawn(Database db, OrbitLayout layout, ShipController player, PlayerTurret turret)
        {
            if (layout.alienOrbit || layout.systemIndex < 0 || Shop.FirstMounted(db, 33) == null) return null;
            var (item, p) = TopPlasma(db, layout.systemIndex);
            var system = db.Systems.Find(s => s.index == layout.systemIndex);
            if (layout.systemIndex != 10 && item == 204 && (system?.jumpRoutesTo == null || system.jumpRoutesTo.Count == 0)) return null;
            bool tutorial = !Session.FreePlay && Session.CampaignMission == 142 && layout.stationIndex == 79;
            int count = (int)((tutorial ? 3f : 0f) + (Random.Range(0, 4) + 4) * (p / 100f));
            if (count <= 0) return null;
            var go = new GameObject("Gas clouds");
            var field = go.AddComponent<GasCloudField>();
            field.Setup(db, player, turret, item);
            var centre = OrbitLayout.ToUnity(layout.asteroidCentre);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos;
                if (tutorial && i == 0) pos = new Vector3(92000, 0, 36000);
                else
                {
                    int guard = 0;
                    do pos = new Vector3(Random.Range(0, 160000) - 80000, Random.Range(0, 160000) - 80000, Random.Range(0, 160000) - 80000);
                    while ((pos.magnitude < 25000f || (OrbitLayout.ToUnity(pos) - centre).magnitude / M < 30000f) && ++guard < 1000);
                }
                field.Add(OrbitLayout.ToUnity(pos));
            }
            return field;
        }

        /// <summary>Galaxy::getPlasmaProbabilities 0x1a518c: the top plasma and its p.</summary>
        public static (int item, int p) TopPlasma(Database db, int system)
        {
            var list = new List<(int item, int p)>();
            for (int item = 201; item <= 204; item++)
            {
                var it = db.Item(item);
                int p = it == null ? 0 : 100 - Shop.Distance(db, system, it.lowestPriceSystem);
                list.Add((item, p < 50 ? 0 : p));
            }
            var sorted = list.Select((e, i) => (e, i)).OrderByDescending(x => x.e.p).ThenBy(x => x.i).Select(x => x.e).ToList();
            for (int k = 0; k < sorted.Count; k++) if (sorted[k].p > 0) sorted[k] = (sorted[k].item, sorted[k].p - 2 * k);
            return sorted[0];
        }

        void Setup(Database database, ShipController player, PlayerTurret playerTurret, int item)
        {
            db = database;
            ship = player;
            turret = playerTurret;
            PlasmaItem = item;
            cloudPrefab = AssembledObject.LoadPrefab(db.AssemblyByName(Assemblies[Mathf.Clamp(item - 201, 0, 3)]));
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            collected = SupernovaAssets.Load()?.plasmaCollected;
            Gun.Detonated += OnDetonated;
        }

        void OnDestroy() => Gun.Detonated -= OnDetonated;

        void Add(Vector3 position)
        {
            var c = new Cloud { position = position };
            if (cloudPrefab != null)
            {
                c.go = Instantiate(cloudPrefab, position, Quaternion.identity, transform);
                c.go.name = "Gas cloud";
                GunRig.StripForFx(c.go);
                // The assembly holds the cloud and (as its child) the spark billboard: keep one spark as the template.
                foreach (Transform t in c.go.GetComponentsInChildren<Transform>(true))
                    if (t != c.go.transform && t.name.Contains("sn_plasma_"))
                    {
                        if (sparkTemplate == null)
                        {
                            sparkTemplate = Instantiate(t.gameObject, transform).transform;
                            sparkTemplate.gameObject.SetActive(false);
                            sparkBaseScale = t.lossyScale;
                        }
                        t.gameObject.SetActive(false);
                    }
            }
            clouds.Add(c);
        }

        /// <summary>Gun::ignite of an ionizing missile (sort 34): PlayerGasCloud::explode on the clouds within its reach.</summary>
        void OnDetonated(Gun gun, Vector3 at)
        {
            if (gun == null || gun.kind != Gun.Kind.Ionizing || gun.magnitude <= 0f) return;
            var item = db.Item(gun.itemIndex);
            int attr56 = item != null ? item.Attr(56) : 50;
            foreach (var c in clouds)
            {
                if (c.exploded) continue;
                float d = (at - c.position).magnitude / M;
                if (d >= gun.magnitude) continue;
                Explode(c, at, d, gun.magnitude, attr56);
            }
        }

        void Explode(Cloud c, Vector3 at, float d, float magnitude, int attr56)
        {
            c.exploded = true;
            c.ageMs = 0f;
            if (c.go != null) foreach (var r in c.go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            float f = 1.5f - d / magnitude;
            int n = (int)((f * 130f / 1.5f + 10f) * (attr56 / 100f));
            for (int i = 0; i < n; i++)
            {
                var start = at + new Vector3(f * Random.Range(0, 10000) - f * 5000f, f * Random.Range(0, 10000) - f * 5000f, f * Random.Range(0, 10000) - f * 5000f) * M;
                float k = Random.Range(0, 200) / 200f * 3f + 3f;
                var s = new Spark
                {
                    dir = (c.position - start).sqrMagnitude > 1e-6f ? (c.position - start).normalized : Random.onUnitSphere,
                    speed = k * 7f, minSpeed = k, lifeMs = Random.Range(0, 14000) + 8000,
                };
                if (sparkTemplate != null)
                {
                    s.tr = Instantiate(sparkTemplate.gameObject, start, Quaternion.identity, transform).transform;
                    s.tr.gameObject.SetActive(true);
                    s.tr.name = "Plasma spark";
                }
                c.sparks.Add(s);
            }
            if (n <= 0) c.done = true;
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            turret = FindTurret();
            FlushMessage(dtMs);
            bool collecting = turret != null && turret.IsCollector && turret.InTurretView;
            var gun = turret != null ? turret.GunPosition : ship != null ? ship.transform.position : Vector3.zero;
            foreach (var c in clouds)
            {
                if (!c.exploded || c.done) continue;
                c.ageMs += dtMs;
                bool anyAlive = false;
                foreach (var s in c.sparks)
                {
                    if (!s.Alive) { if (s.tr != null && s.tr.gameObject.activeSelf) s.tr.gameObject.SetActive(false); continue; }
                    s.lifeMs -= dtMs;
                    s.speed = Mathf.Max(s.speed - dtMs * 0.08f, s.minSpeed);
                    var pos = s.tr != null ? s.tr.position : c.position;
                    float dt = (gun - pos).magnitude / M;
                    if (dt < CollectUnits && c.ageMs >= CollectDelayMs && collecting && s.lifeMs >= -1499f)
                    {
                        s.lifeMs = -1500f;   // collected
                        Collect();
                        s.scale = 0f;
                    }
                    else if (s.lifeMs < 1f) s.scale = Mathf.Max(0f, s.lifeMs / 1500f + 1f);
                    else if (dt < ShrinkUnits) s.scale = (dt - CollectUnits) / 2700f;
                    else s.scale = 1f;
                    bool pulled = collecting && c.ageMs >= CollectDelayMs && InSight(pos);
                    if (pulled) pos += (gun - pos).normalized * turret.PullSpeed * dtMs * M;
                    else pos += s.dir * s.speed * dtMs * M;
                    if (s.tr != null) { s.tr.position = pos; s.tr.localScale = sparkBaseScale * Mathf.Max(0f, s.scale); }
                    if (s.Alive) anyAlive = true;
                }
                if (!anyAlive) c.done = true;
            }
        }

        void LateUpdate()
        {
            // The "_lookat" billboards face the camera (setDirection(-cameraDir, cameraUp)).
            var cam = Camera.main;
            if (cam == null) return;
            var rot = cam.transform.rotation;
            foreach (var c in clouds)
            {
                if (c.go != null && !c.exploded) c.go.transform.rotation = rot;
                foreach (var s in c.sparks) if (s.tr != null && s.tr.gameObject.activeSelf) s.tr.rotation = rot;
            }
        }

        /// <summary>Radar::draw's collector scope: in the turret's aim and within attr 51 of the player.</summary>
        bool InSight(Vector3 pos)
        {
            if (turret == null || ship == null) return false;
            if ((pos - ship.transform.position).magnitude / M > turret.CollectRange) return false;
            var to = pos - turret.GunPosition;
            return to.sqrMagnitude > 1e-6f && Vector3.Angle(turret.AimForward, to) < SightDegrees;
        }

        /// <summary>+1 t of the plasma (Hud::catchCargo), or the hold-full message. The sound is stopped and restarted
        /// (FModSound::stop + play 0x8d0): one at a time however many sparks arrive together.</summary>
        void Collect()
        {
            if (collected != null && collected.Length > 0) { sfx.Stop(); sfx.clip = collected[UnityEngine.Random.Range(0, collected.Length)]; sfx.volume = Settings.SfxVolume; sfx.Play(); }
            if (Shop.FreeCargo(db) < 1) { if (!fullShown) Message?.Invoke(Localization.Get(322)); fullShown = true; return; }   // Cargo hold is full.
            fullShown = false;
            Shop.AddToCargo(PlasmaItem, 1);
            pendingTons++;
            PlasmaCollected?.Invoke(PlasmaItem);
        }
        int pendingTons;
        float pendingMs;
        bool fullShown;

        /// <summary>The catch message once a burst has come in: "+12t Green Plasma" (one line instead of one per spark).</summary>
        void FlushMessage(float dtMs)
        {
            if (pendingTons == 0) { pendingMs = 0f; return; }
            pendingMs += dtMs;
            if (pendingMs < 600f) return;
            Message?.Invoke($"+{pendingTons}t {Localization.Get(1274 + PlasmaItem)}");
            pendingTons = 0;
            pendingMs = 0f;
        }

        /// <summary>Any spark in the collector's sight right now (the crosshair's "plasma in range", Radar+0x130).</summary>
        public bool PlasmaInRange
        {
            get
            {
                if (turret == null || !turret.IsCollector || !turret.InTurretView) return false;
                foreach (var c in clouds)
                    if (c.exploded && !c.done)
                        foreach (var s in c.sparks) if (s.Alive && s.tr != null && InSight(s.tr.position)) return true;
                return false;
            }
        }
    }
}
