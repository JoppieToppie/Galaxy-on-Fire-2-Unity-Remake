// OrbitLayout.cs
// Everything the original lays out for one station orbit (a flight level), computed exactly like the game does:
// the engine RNG is java.util.Random (JavaRandom), seeded with the station index, so the jumpgate spot, the
// sky rotation, the sun/planet layout and the asteroid field centre are the same on every visit.
// Plain C# (no scene objects); SpaceLevel turns it into a scene. All positions are GAME units.
//
// Sources (Reference/research/space_level_setup.md, space_backdrop.md, space_props.md):
//   Level::createSpace 0xbbba0      jumpgates (seed 2*station), sky rotation (seed 2*station, separate sequence)
//   StarSystem::StarSystem 0x15c200 sun + planets (seed 300*station), StarSystem::initLight 0x15d080 lights/fog
//   Level::createAsteroids 0xbcfd0  asteroid count + field centre (seed station)
//   Level::initParticleSystems 0xcc990 fog sprite tint

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class OrbitLayout
    {
        public class Planet
        {
            public int station;          // station whose planet this is
            public int slot;             // yaw = slot * 0xAAA / 65536 * 2pi
            public float pitch, yaw;     // radians, game convention (Rx(pitch) * Ry(yaw))
            public float scale;          // of the 65000-unit plane quad
            public bool flip;            // original's rotate(0, pi, 0): lit rim toward the sun
            public string texture;       // file name (no extension), Textures/<pack>/planets/
            public bool ring;            // draws sn_planet_ring at 4x behind/around it
            public bool isOrbitPlanet;   // the current station's own planet, straight ahead along game -Z
        }

        public int stationIndex, systemIndex;
        public int systemTexture, stationTexture;
        public int raceId;
        /// <summary>A station to lock, fly to and dock at (not Status::inEmptyOrbit).</summary>
        public bool hasStation;
        /// <summary>Level::createSpace builds the PlayerStation: wherever there is a station, and in the empty orbits of 27,
        /// 110 and 111 too (only docking is off there: the wrecks of Valpatro and Luur stay).</summary>
        public bool stationObject;
        /// <summary>Status::inAlienOrbit: the Void's home orbit (Session.VoidOrbit): Void station, sky 010, Void asteroids.</summary>
        public bool alienOrbit;
        /// <summary>Remake: the space between the systems, not this orbit (InterstellarSpace.Clear).</summary>
        public bool interstellar;
        /// <summary>Status::inPlanetRingOrbit (stations 120 / 126 / 130 / 132): the orbit planet has no camera zoom.</summary>
        public bool ringOrbit;

        // Level::createSpace
        public bool hasJumpgate;
        public Vector3 jumpgate;         // landmark 1 (visible gate)
        public Vector3 hiddenJumpgate;   // landmark 2 (arrival point when travelling in)
        /// <summary>Remake mods (stations.json "orbit" "arrival"): where every arrival by travel comes in, facing the station.</summary>
        public Vector3? arrival;
        public int hiddenGateAngle;      // A2 (65536 per turn)
        public Vector3 skyEuler;         // radians, R_sky = Rx * Ry * Rz (game)
        public bool skySunAligned;       // system 27: the sky's +Y points at the sun

        // StarSystem
        public int sunSlot;
        public float sunPitch;
        public Vector3 lightDirection;   // unit vector toward the sun (game space)
        public string sunTexture;
        public float sunScale = 0.2288818359375f;   // 15000 / 65536
        /// <summary>StarSystem+0xc = Status::inSupernovaSystem (system 27 before campaign 0x9e): the sun's own sizes, drawn
        /// without roll or swelling, and the sn_sun_011 glow / streak (see Backdrop).</summary>
        public bool supernovaSun;
        public int flareColor;
        public readonly List<Planet> planets = new List<Planet>();

        // StarSystem::initLight
        public Color sunColor, planetLightColor;
        public bool fog;
        public Color fogColor;
        public float fogEnd;

        // Level::createAsteroids (stable part)
        public int asteroidCount;
        public Vector3 asteroidCentre;
        public int asteroidType;         // 0 default, 1 void, 2 ice (Beidan), 3 magma
        /// <summary>The field's size in game units: the small asteroids in a cube of this side, the big ones in 0.6 of it
        /// (Level::createAsteroids: 100000 / 60000; remake mods change it).</summary>
        public float asteroidSpread = 100000f;
        /// <summary>Remake mods: the field's ores (item, chance weight), null: the system's (OreProbabilities).</summary>
        public List<(int item, int chance)> modOres;

        // Level::initParticleSystems
        public Color dustFogTint;

        public const float PlaneSize = 65000f;          // plane.fbx is +-32500 units
        public const float BackdropDistance = 20000f;   // moveForward(-20000)
        public const int GateRadius = 7500, GateRadiusVossk = 11250;
        public static readonly Vector3 UndockPosition = new Vector3(10f, 10f, 10000f);   // Level::init 0xbb7a8
        public const int UndockYaw = 1600;              // +-1600/65536 turn, random sign

        static readonly int[] RingStations = { 120, 126, 130, 132 };
        static readonly int[] EmptyOrbits = { 102, 103, 104, 109, 110, 132, 133, 134 };   // Status::inEmptyOrbit (mission-independent part)
        static readonly int[] StationObjectInEmptyOrbit = { 27, 110, 111 };              // Level::createSpace 0xbc0f6

        /// <summary>Remake multiplayer (Session.CompletedWorld): Ginoya's Naneroh, Valpatro and Luur, empty or wrecked after
        /// the supernova in the original, stand intact with their shops (OrbitBuilder.StationLook / StationAssembly).</summary>
        public static bool IsRebuiltGinoya(int station) => station >= 109 && station <= 111;

        /// <summary>Status::inEmptyOrbit 0xb8ee8 for a real station at campaign index 'mission' (free play and multiplayer: 20):
        /// the orbit has no station to dock at (Level::collideStation, MGame::dockEvent), none in the autopilot menu
        /// (AutoPilotList / Hud::initHudMenu). Always 102-104, 109, 110, 132-134; Var Hastra (78) at index 0 / 1 (the prologue
        /// and the rescue); Luur (111) from 0x5e (after the evacuation, a wreck); Valkyrie (101) from 0x54 (gone with Alice
        /// after the add-on). The alien orbit's rule (Status+0x78, the Void station gone at 43-83 and from 154) is Build's.</summary>
        public static bool IsEmptyOrbit(int station, int mission)
        {
            if (Session.CompletedWorld && IsRebuiltGinoya(station)) return false;   // remake multiplayer: intact and open
            if (System.Array.IndexOf(EmptyOrbits, station) >= 0) return true;
            return (station == 78 && mission <= 1) || (station == 111 && mission > 0x5d) || (station == 101 && mission >= 0x54);
        }

        static readonly string[] SunTextures =
        {
            "sun_000", "sun_001", "sun_002", "sun_003", "sun_004", "sun_005", "sun_006", "sun_007", "sun_008", "sun_009",
            "sun_010", "sun_001", "sun_008", "sun_004", "sun_000", "sn_supernova", "sun_007", "sun_002", "sun_006",
        };

        static readonly int[] FlareColors = { 3, 1, 3, 2, 0, 3, 1, 4, 2, 4, 1, 3, 1, 3, 2, 3, 3, 0, 2, 1, 2, 3, 2, 0, 3, 1, 2, 5, 3, 3, 3, 3, 3, 3 };

        // DAT_00258b0c: sun colour per system textureIndex (LIGHT0 = clamp(15 * c, 0, 2), ambient 0.15 * c)
        static readonly float[] SunColors =
        {
            .21f, .32f, .25f,  .50f, .29f, .28f,  .24f, .40f, .37f,  .22f, .32f, .43f,  .18f, .23f, .27f,
            .38f, .13f, .11f,  .34f, .34f, .30f,  .24f, .26f, .25f,  .21f, .23f, .27f,  .43f, .47f, .47f,
            .43f, .21f, .34f,  .85f, .41f, .14f,  .08f, .24f, .48f,  .20f, .25f, .20f,  .30f, .35f, .15f,
            1.0f, .52f, .24f,  .39f, .48f, .44f,  .41f, .52f, .58f,  .67f, .63f, .46f,
        };

        // DAT_00258bf0: planet bounce light per station textureIndex (LIGHT1 = 1.5 * c)
        static readonly float[] PlanetLightColors =
        {
            .29f, .35f, .31f,  .25f, .22f, .14f,  .20f, .17f, .09f,  .30f, .27f, .31f,  .25f, .34f, .41f,
            .39f, .165f, .045f, .37f, .43f, .26f, .42f, .22f, .11f,  .18f, .25f, .28f,  .075f, .065f, .045f,
            .20f, .43f, .35f,  .27f, .09f, .04f,  .38f, .29f, .16f,  .06f, .05f, .03f,  .11f, .10f, .11f,
            .25f, .23f, .20f,  .25f, .19f, .135f, .17f, .35f, .48f,  .07f, .07f, .07f,  .26f, .29f, .31f,
            .13f, .10f, .06f,  .31f, .31f, .21f,  .39f, .32f, .25f,  .64f, .50f, .67f,  .45f, .38f, .34f,
            .48f, .52f, .58f,  .21f, .27f, .34f,
        };

        // DAT_00252ca0: fog sprite colour per system textureIndex (x 0.6, alpha 0xbb)
        static readonly byte[] DustFogTints =
        {
            0, 160, 32,  140, 20, 2,  0, 109, 123,  44, 155, 216,  122, 122, 122,  206, 90, 70,  105, 160, 125,
            167, 160, 126,  126, 181, 159,  206, 222, 225,  146, 116, 212,  219, 105, 35,  255, 255, 255,
            154, 228, 255,  174, 183, 125,  219, 105, 35,  71, 102, 94,  115, 141, 149,  171, 160, 117,
        };

        static readonly string[] JumpgateByRace = { "jumpgate_terran", "jumpgate_vossk_anim", "jumpgate_nivelian", "jumpgate_midorian" };

        public string JumpgateAssembly => JumpgateByRace[Look >= 0 && Look < 4 ? Look : 0];
        /// <summary>The system's own gate (a mod's systems.json "jumpgate", Modding.ModWorld.GateModelOf), else null: the race's.</summary>
        public string ModJumpgateAssembly => Modding.ModWorld.GateModelOf(systemIndex)?.Assembly;
        public int JumpgateRadius => Modding.ModWorld.GateModelOf(systemIndex) is Modding.ModWorld.StationModel g && g.gateRadius > 0f
            ? Mathf.RoundToInt(g.gateRadius) : Look == 1 ? GateRadiusVossk : GateRadius;
        /// <summary>The race whose tables the orbit uses (a mod race's look, Standing.Look).</summary>
        int Look => Flight.Standing.Look(raceId);

        static float Angle(int v) => v * 1.52587890625e-05f * 6.2831855f;   // 65536 per turn

        /// <summary>Local +Z of Rx(pitch) * Ry(yaw): the direction StarSystem places a body along (at -20000 * dir).</summary>
        public static Vector3 Direction(float pitch, float yaw) =>
            new Vector3(Mathf.Sin(yaw), -Mathf.Sin(pitch) * Mathf.Cos(yaw), Mathf.Cos(pitch) * Mathf.Cos(yaw));

        public static OrbitLayout Build(Database db, int stationIndex)
        {
            var st = db.Stations.Find(s => s.index == stationIndex);
            var sys = st != null ? db.Systems.Find(s => s.index == st.system) : null;
            bool alien = stationIndex == Session.VoidOrbit;
            int mission = Session.WorldIndex;
            var o = new OrbitLayout
            {
                stationIndex = stationIndex,
                systemIndex = sys != null ? sys.index : -1,
                systemTexture = sys != null ? sys.textureIndex : 10,
                stationTexture = st != null ? st.textureIndex : 23,
                // A mobile station (ModWorld "mobile") keeps its own race wherever it is.
                raceId = Modding.ModWorld.MobileOf(stationIndex) is Modding.ModWorld.MobileSpec ms ? ms.race : sys != null ? sys.raceId : 0,
                hasStation = st != null && !IsEmptyOrbit(stationIndex, mission),
                alienOrbit = alien,
            };
            o.stationObject = o.hasStation || (st != null && System.Array.IndexOf(StationObjectInEmptyOrbit, stationIndex) >= 0);
            // Status::inEmptyOrbit: the alien orbit has its Void station until the mother ship is destroyed (index 43..83).
            if (alien) o.hasStation = o.stationObject = Story.Index < 43 || Story.Index > 83 && Story.Index < 154;
            o.BuildGates(sys);
            o.BuildSky();
            o.BuildStarSystem(db, sys);
            o.BuildLights();
            o.BuildAsteroidField();
            if (!alien) o.ApplyMod(Modding.ModWorld.OrbitOf(stationIndex), st);
            return o;
        }

        /// <summary>Remake mods: stations.json "orbit" over the seeded layout (Modding.ModWorld.OrbitSpec). The station stays
        /// the orbit's centre: the gate, the field and the arrival are placed around it.</summary>
        void ApplyMod(Modding.ModWorld.OrbitSpec m, StationData st)
        {
            if (m == null) return;
            if (m.station == 0) hasStation = stationObject = false;
            else if (m.station == 1) hasStation = stationObject = st != null;
            else if (m.station == 2) { hasStation = false; stationObject = st != null; }
            if (m.jumpgate == true && !hasJumpgate)
            {
                // The spot BuildGates would have rolled for a gate orbit (its first draws, seed 2 * station).
                var rnd = new JavaRandom(2L * stationIndex);
                int s = rnd.NextInt(2), k = rnd.NextInt(500);
                int a = (s == 0 ? -(250 + k) : 250 + k) * 16;
                float r = 90000f + 3f * a, ang = a / 65536f * 2f * Mathf.PI;
                jumpgate = new Vector3(r * Mathf.Sin(ang), 0f, r * Mathf.Cos(ang));
            }
            if (m.jumpgate.HasValue) hasJumpgate = m.jumpgate.Value;
            if (m.jumpgatePosition.HasValue) jumpgate = m.jumpgatePosition.Value;
            if (m.arrival.HasValue) arrival = m.arrival.Value;
            if (m.asteroids == false) asteroidCount = 0;
            if (m.asteroidCount.HasValue) asteroidCount = m.asteroidCount.Value;
            if (m.asteroidCentre.HasValue) asteroidCentre = m.asteroidCentre.Value;
            if (m.asteroidSpread.HasValue) asteroidSpread = m.asteroidSpread.Value;
            if (m.asteroidType >= 0) asteroidType = m.asteroidType;
            if (m.ores != null) modOres = m.ores;
            if (m.fog == false) fog = false;
            else if (m.fog == true)
            {
                if (!fog) { fogColor = new Color(0.45f, 0.45f, 0.5f); fogEnd = 100000f; }
                fog = true;
                if (m.fogColor.HasValue) fogColor = m.fogColor.Value;
                if (m.fogEnd.HasValue) fogEnd = m.fogEnd.Value;
            }
            if (m.sun.HasValue)
            {
                lightDirection = ModDirection(m.sun.Value.x, m.sun.Value.y);
                sunPitch = Mathf.Asin(lightDirection.y);
            }
            var own = planets.Find(p => p.isOrbitPlanet);
            if (own != null)
            {
                if (m.planetSize.HasValue) own.scale *= m.planetSize.Value;
                if (m.planetYaw.HasValue || m.planetPitch.HasValue)
                {
                    // The backdrop places a body at -Direction(pitch, yaw), whose pitch tips the other way past 90 deg of yaw:
                    // the same direction in its own angles.
                    var d = ModDirection(m.planetYaw ?? 0f, m.planetPitch ?? 0f);
                    own.yaw = Mathf.Asin(Mathf.Clamp(-d.x, -1f, 1f));
                    own.pitch = Mathf.Atan2(d.y, -d.z);
                }
            }
        }

        /// <summary>Remake mods: a direction from the station (game space, unit) by yaw / pitch in degrees: yaw 0 where the game's
        /// orbit planet sits (game -z: behind a ship launching from the station), 90 to that ship's right (game -x), 180 ahead
        /// of it; pitch up from the horizon.</summary>
        public static Vector3 ModDirection(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = Mathf.Clamp(pitchDeg, -89f, 89f) * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), -Mathf.Cos(y) * Mathf.Cos(p));
        }

        // Level::createSpace: landmark 1 only in the gate orbit, landmark 2 (hidden) always.
        void BuildGates(SystemData sys)
        {
            hasJumpgate = sys != null && sys.jumpgateStation == stationIndex;
            var rnd = new JavaRandom(2L * stationIndex);
            int a = 0;
            for (int i = hasJumpgate ? 1 : 2; i <= 2; i++)
            {
                int s = rnd.NextInt(2), k = rnd.NextInt(500);
                a += (s == 0 ? -(250 + k) : 250 + k) * 16;
                float r = (i == 1 ? 90000f : 120000f) + 3f * a;
                float ang = a / 65536f * 2f * Mathf.PI;
                var pos = new Vector3(r * Mathf.Sin(ang), 0f, r * Mathf.Cos(ang));
                if (i == 1) jumpgate = pos; else { hiddenJumpgate = pos; hiddenGateAngle = a; }
            }
            // Alien orbit: landmark 2 (the arrival point) at (0, rand(20000) - 10000, rand(50000) + 170000), facing the origin.
            if (alienOrbit) hiddenJumpgate = new Vector3(0f, Random.Range(0, 20000) - 10000, Random.Range(0, 50000) + 170000);
        }

        // Level::createSpace: R_sky = Rx * Ry * Rz from 3 draws (seed 2*station); none in the fog skies (17, 18).
        void BuildSky()
        {
            if (systemTexture == 17 || systemTexture == 18) { skyEuler = Vector3.zero; return; }
            var rnd = new JavaRandom(2L * stationIndex);
            skyEuler = new Vector3(Angle(rnd.NextInt(65536)), Angle(rnd.NextInt(65536)), Angle(rnd.NextInt(65536)));
            skySunAligned = systemIndex == 27;
        }

        // StarSystem::StarSystem: sun at index 0, then one planet per station of the system (seed 300*station).
        void BuildStarSystem(Database db, SystemData sys)
        {
            sunTexture = Modding.ModWorld.SunTexture(systemIndex) ?? SunTextures[Mathf.Clamp(systemTexture, 0, SunTextures.Length - 1)];   // remake mods: a system's own sun
            flareColor = systemIndex >= 0 && systemIndex < FlareColors.Length ? FlareColors[systemIndex] : 3;
            // StarSystem::StarSystem in the supernova system (Status::inSupernovaSystem: system 27 before 0x9e): the sun
            // billboard at 0.99182 (1.37329 from campaign 0x6a), its streak layer drawn with sn_sun_011 (0x2dde). At 0x59
            // (before the explosion) and past 0x9d the sun itself is sn_sun_011, flare colour 3; past 0x9d the system is no
            // longer the supernova system, so the sun has the normal size and look.
            if (systemIndex == 27)
            {
                int mission = Session.WorldIndex;
                if (mission < 0x9e)
                {
                    sunScale = mission < 0x6a ? 0.9918212890625f : 1.373291015625f;
                    supernovaSun = true;
                }
                if (mission == 0x59 || mission > 0x9d) { sunTexture = "sn_sun_011"; flareColor = 3; }
            }
            var rnd = new JavaRandom(300L * stationIndex);
            var occupied = new bool[24];

            int r = rnd.NextInt(14);
            int k = stationTexture - 9;
            if (k >= 0 && k < 13 && ((0x1a31 >> k) & 1) != 0) sunSlot = k == 12 ? 4 : 6;
            else sunSlot = stationTexture == 22 ? 16 : r + 5;
            occupied[sunSlot] = true;
            sunPitch = Angle(rnd.NextInt(4096) - 2048);
            lightDirection = -Direction(sunPitch, Angle(sunSlot * 0xAAA));

            if (sys == null)
            {
                // The alien orbit (StarSystem::StarSystem's Void branch: Status::getSystem() is 0 there): the sun sun_010 straight
                // ahead (moveForward(-20000), no rotation), planet_void_big at rotation (0, 5.2347097, 0) (60 deg right of it),
                // unseeded size rand(20000) + 20000, no flip, no zoom (Backdrop).
                if (alienOrbit)
                {
                    sunSlot = 0;
                    sunPitch = 0f;
                    lightDirection = -Direction(0f, 0f);
                    planets.Add(new Planet { station = stationIndex, isOrbitPlanet = true, scale = (Random.Range(0, 20000) + 20000) / 65536f,
                                             yaw = 5.2347097f, flip = false, texture = PlanetTexture(23, true) });
                }
                return;
            }
            ringOrbit = System.Array.IndexOf(RingStations, stationIndex) >= 0;
            // StarSystem::StarSystem picks "own planet or another" by the system's list order but takes the texture from the
            // stations loaded in file order (FileRead::loadStationsBinary: ascending index) at the same position: K'ontrr
            // (system 20, [62, 59, 58, 63, 64]) swaps 58's and 62's planet textures. The own planet's size still follows the
            // real station's texture.
            var byFileOrder = new List<int>(sys.stations);
            byFileOrder.Sort();
            // StarSystem::StarSystem 0x15c89a: mission 0 (the prologue and the main menu's backdrop, Status::resetGame) halves the
            // orbit planet (before the size overrides by texture, which replace it).
            bool missionZero = Session.WorldIndex == 0;
            for (int i = 0; i < sys.stations.Count; i++)
            {
                int stIdx = sys.stations[i];
                var st = db.Stations.Find(s => s.index == stIdx);
                var shown = db.Stations.Find(s => s.index == byFileOrder[i]);
                int t = st != null ? st.textureIndex : 0;
                int shownTexture = shown != null ? shown.textureIndex : t;
                var p = new Planet { station = stIdx, ring = System.Array.IndexOf(RingStations, stIdx) >= 0 };
                if (stIdx == stationIndex)
                {
                    int s = rnd.NextInt(20000) + 20000;
                    if (missionZero) s = (int)(s * 0.5f);
                    if (!ringOrbit)
                    {
                        if (t < 18 && ((1 << t) & 0x21840) != 0) s = rnd.NextInt(15000) + 35000;
                        else if (t < 18 && ((1 << t) & 0x10200) != 0) s = rnd.NextInt(13000) + 32500;
                    }
                    else s = 26000;
                    occupied[0] = true;
                    p.isOrbitPlanet = true;
                    p.scale = s / 65536f;
                    p.flip = sunSlot >= 12;
                    p.texture = Modding.ModWorld.PlanetTexture(shown != null ? shown.index : stIdx) ?? PlanetTexture(shownTexture, true);   // remake mods: a station's own planet
                    p.ring = false;   // the ring orbit shows the ring sky layer instead
                }
                else
                {
                    int slot;
                    do slot = rnd.NextInt(11) + 7;
                    while (Mathf.Abs(slot - sunSlot) < 3 || occupied[slot]);
                    occupied[slot] = true;
                    p.slot = slot;
                    p.scale = (float)((rnd.NextInt(40) * 0.01 + 0.800000011920929) * 0.03509521484375);
                    p.flip = slot > sunSlot;
                    p.pitch = Angle(rnd.NextInt(4096) - 2048);
                    p.yaw = Angle(slot * 0xAAA);
                    p.texture = Modding.ModWorld.PlanetTexture(shown != null ? shown.index : stIdx) ?? PlanetTexture(shownTexture, false);
                }
                planets.Add(p);
            }
        }

        static string PlanetTexture(int t, bool big)
        {
            string size = big ? "big" : "small";
            if (t < 20) return $"planet_{t:000}_{size}";
            if (t < 23) return $"v_planet_{t:000}_{size}";
            if (t == 23) return big ? "planet_void_big" : "v_planet_022_small";
            return $"sn_planet_{t:000}_{size}";
        }

        // StarSystem::initLight
        void BuildLights()
        {
            int s = Mathf.Clamp(systemTexture, 0, 18) * 3;
            sunColor = new Color(SunColors[s], SunColors[s + 1], SunColors[s + 2]);
            if (Modding.ModWorld.SunColor(systemIndex, out var modSun)) sunColor = modSun / 15f * 1.5f;   // remake mods: 1 = a bright light (x15 below, clamped at 2)
            int p = Mathf.Clamp(stationTexture, 0, 26) * 3;
            if (alienOrbit) p = 0x17;   // initLight 0x15d080: the alien orbit reads the table at flat element 23, not row 23
            planetLightColor = new Color(PlanetLightColors[p], PlanetLightColors[p + 1], PlanetLightColors[p + 2]) * 1.5f;
            // initParticleSystems: x0.6 and alpha 0xbb, in the supernova system (Status::inSupernovaSystem) x0.5 and opaque
            bool supernova = Shop.InSupernovaSystem(systemIndex, stationIndex);
            float dust = supernova ? 0.5f : 0.6f;
            dustFogTint = new Color32((byte)(DustFogTints[s] * dust), (byte)(DustFogTints[s + 1] * dust), (byte)(DustFogTints[s + 2] * dust), supernova ? (byte)0xff : (byte)0xbb);
            if (alienOrbit) dustFogTint = new Color32(0x92, 0x74, 0xd4, 0xbb);   // initParticleSystems: 0x9274d4 in the alien orbit
            switch (systemTexture)
            {
                case 11: fog = true; fogColor = new Color32(0xdb, 0x69, 0x23, 255); fogEnd = 50000f; break;
                case 12: fog = true; fogColor = new Color32(0x16, 0x3e, 0x7c, 255); fogEnd = 50000f; break;
                // StarSystem::initLight case 0xf returns before the fog at campaign 0x59 (89's supernova cutscene: no haze).
                case 15: fog = Session.WorldIndex != 89; fogColor = new Color32(0x82, 0x44, 0x1f, 255); fogEnd = 100000f; break;
                case 16: fog = true; fogColor = new Color32(0x47, 0x66, 0x5e, 255); fogEnd = 150000f; break;
                case 17: fog = true; fogColor = new Color32(0x73, 0x8d, 0x95, 255); fogEnd = 150000f; break;
                case 18: fog = true; fogColor = new Color32(0xab, 0xa0, 0x75, 255); fogEnd = 150000f; break;
            }
        }

        /// <summary>LIGHT0 diffuse: clamp(15 * sunColour, 0, 2) per channel (white x2 for most systems).</summary>
        public Color SunLightColor => new Color(Mathf.Min(sunColor.r * 15f, 2f), Mathf.Min(sunColor.g * 15f, 2f), Mathf.Min(sunColor.b * 15f, 2f));

        // Level::createAsteroids: count and centre are seeded (stable), the asteroids themselves are not.
        void BuildAsteroidField()
        {
            var rnd = new JavaRandom(stationIndex);
            asteroidCount = rnd.NextInt(40) + 40;
            int cx = rnd.NextInt(100000) - 50000, cy = rnd.NextInt(100000) - 50000, cz = rnd.NextInt(100000) + 20000;
            asteroidCentre = new Vector3(cx, cy, cz);
            asteroidType = alienOrbit ? 1 : systemIndex == 22 ? 2 : 0;   // space_props.md: the alien orbit's Void asteroids
            // space_props.md 2: the alien orbit's field sits at (-30000, 0, 30000).
            if (alienOrbit) asteroidCentre = new Vector3(-30000f, 0f, 30000f);
        }

        public string AsteroidAssembly => asteroidType switch { 1 => "asteroid_void", 2 => "v_asteroid_ice", 3 => "sn_asteroid_magma", _ => "asteroid_01" };

        /// <summary>Asteroid mesh bounding radius in game units (sidecar bsphere): hit radius = this * scale * 0.7.</summary>
        public float AsteroidMeshRadius => asteroidType switch { 1 => 6064f, 2 => 2835f, _ => 3547f };

        // ---- game -> Unity ---------------------------------------------------------------------------------------

        public const float MetersPerUnit = 0.05f;

        /// <summary>Game position to Unity: (x, y, -z) * 0.05.</summary>
        public static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * MetersPerUnit;

        /// <summary>Where a jump in puts the ship, and the way it faces: the station. 'previousStation': the one it came from.</summary>
        public void ArrivalPose(int previousStation, out Vector3 position, out Quaternion facing)
        {
            position = ArrivalPoint(previousStation);
            facing = Quaternion.LookRotation(-position.normalized, Vector3.up);
            // Level::init: arriving in Loma's black market (system 25) puts the player 40 000 further along game +z.
            if (systemIndex == 25) position += ToUnity(new Vector3(0f, 0f, 40000f));
        }

        Vector3 ArrivalPoint(int previousStation)
        {
            if (arrival.HasValue) return ToUnity(arrival.Value);   // remake mods: the orbit's own
            if (hasJumpgate || alienOrbit) return ToUnity(hiddenJumpgate);
            var from = planets.Find(p => p.station == previousStation);
            if (from == null) return ToUnity(new Vector3(0f, 0f, 100000f));
            return ToUnity(-4f * BackdropDistance * Direction(from.pitch, from.yaw));
        }

        /// <summary>Game direction to Unity (unscaled).</summary>
        public static Vector3 DirToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z);

        /// <summary>
        /// Game rotation (AEGeometry::setRotation(x, y, z) = Rx * Ry * Rz, radians) to a Unity rotation for an imported
        /// model. The z mirror turns it into S*R*S = Rx(-x) Ry(-y) Rz(z), and the import rotates every FBX 180 deg about
        /// Y (ModelOrientationPostprocessor), which is undone at the end. Game (0, pi, 0) (stations, gates) = identity.
        /// </summary>
        public static Quaternion RotationToUnity(Vector3 gameEulerRadians) =>
            Quaternion.AngleAxis(-gameEulerRadians.x * Mathf.Rad2Deg, Vector3.right)
            * Quaternion.AngleAxis(-gameEulerRadians.y * Mathf.Rad2Deg, Vector3.up)
            * Quaternion.AngleAxis(gameEulerRadians.z * Mathf.Rad2Deg, Vector3.forward)
            * Quaternion.Euler(0f, 180f, 0f);

        /// <summary>Galaxy::getAsteroidProbabilities 0x1a4fb0: per ore 154..163, p = 100 - distance(system, the ore's cheapest
        /// system), 0 below 50; Void Crystals (164) appended with 0 (100 in an alien orbit); sorted descending (stable),
        /// then p[k] -= 2k for the positive ones.</summary>
        public static List<(int item, int p)> OreProbabilities(Database db, OrbitLayout layout)
        {
            if (layout.modOres != null) return layout.modOres.Select(o => (o.item, o.chance)).ToList();   // remake mods: the orbit's own
            var list = new List<(int item, int p)>();
            bool alien = layout.systemIndex < 0;
            for (int item = 154; item <= 163; item++)
            {
                var it = db.Item(item);
                int p = alien || it == null ? 0 : 100 - Shop.Distance(db, layout.systemIndex, it.lowestPriceSystem);
                list.Add((item, p < 50 ? 0 : p));
            }
            list.Add((164, alien ? 100 : 0));
            var sorted = list.Select((e, i) => (e, i)).OrderByDescending(x => x.e.p).ThenBy(x => x.i).Select(x => x.e).ToList();
            for (int k = 0; k < sorted.Count; k++) if (sorted[k].p > 0) sorted[k] = (sorted[k].item, sorted[k].p - 2 * k);
            // The supernova orbit after step 0x59: every pair's ore becomes Novanium (0xd9), the chances stay.
            // Modified: the Supernova Challenge's orbit too (ChallengeNovaniumOrbit).
            if (NovaniumOrbit(layout) || ChallengeNovaniumOrbit(layout)) for (int k = 0; k < sorted.Count; k++) sorted[k] = (Novanium, sorted[k].p);
            return sorted;
        }

        public const int Novanium = 217;   // 0xd9; its core is 218 (Target.CoreItem)

        /// <summary>Galaxy::getAsteroidProbabilities 0x1a4fb0 with Status::inSupernovaOrbit 0xb9088 (station 0x6d, Naneroh)
        /// and getCurrentCampaignMission > 0x59: the orbit's asteroids are all Novanium (Ginoya's other stations keep their
        /// normal ores). The remake reads Session.WorldIndex, so the finished game's world (multiplayer) has it too.</summary>
        public static bool NovaniumOrbit(OrbitLayout layout) =>
            layout.systemIndex >= 0 && layout.stationIndex == 0x6d && Session.WorldIndex > 0x59;

        /// <summary>Modified (#179, the user's comparison with the original's video): the Supernova Challenge's round at Luur
        /// (111) is ringed with Novanium's magma asteroids too. Kept apart from NovaniumOrbit, which is the original's
        /// Status::inSupernovaOrbit (0x6d only).</summary>
        public static bool ChallengeNovaniumOrbit(OrbitLayout layout) =>
            layout.systemIndex >= 0 && layout.stationIndex == 111 && SupernovaChallenge.Active;
    }
}
