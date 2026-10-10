// GoF2ToolsWindow.cs
// GoF2 > Tools Overview: every GoF2 menu item by group with what it makes, what reads it and when to run it again, and a Run
// button (EditorApplication.ExecuteMenuItem, so the menu stays the one place a tool is wired). The slow or sweeping ones ask
// first. The repo already holds everything these tools make: they are for after changing a builder or the data it reads.

using System;
using UnityEditor;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class GoF2ToolsWindow : EditorWindow
    {
        struct Tool
        {
            public string menu, what;
            public bool confirm;
        }

        struct Group
        {
            public string title, about;
            public Tool[] tools;
        }

        static Tool T(string menu, string what, bool confirm = false) => new Tool { menu = menu, what = what, confirm = confirm };

        static readonly Group[] Groups =
        {
            new Group
            {
                title = "Scenes",
                about = "Rewrite a scene file from code (anything changed in the scene by hand is lost).",
                tools = new[]
                {
                    T("GoF2/Scenes/Main Menu Scene", "Assets/Scenes/MainMenu.unity (build index 0) and the menu's own assets."),
                    T("GoF2/Scenes/Space Scene", "Assets/Scenes/Space.unity, the flight level. Also rebuilds the backdrop materials, sky layers, weapon fx, HUD images, star map and combat assets, and bakes the space skies if they're missing."),
                    T("GoF2/Scenes/Station Scene", "Assets/Scenes/Station.unity, the docked station (hangar and bar) with its music, ambience and visitor prefabs. Makes the space skies, item icons and star map assets if missing."),
                    T("GoF2/Scenes/Add Post Processing To Open Scene", "Adds the global bloom Volume (Settings/GoF2_VolumeProfile) to the scene that is open and turns post-processing on for its camera."),
                },
            },
            new Group
            {
                title = "Build",
                about = "Generated assets. Run one again after changing its builder code or the data it reads; the others are unaffected.",
                tools = new[]
                {
                    T("GoF2/Build/Combat Assets", "Resources/GoF2Combat: crates, wrecks, explosions, tractor beams, the gamma blaze, hit and death sounds, space and battle music."),
                    T("GoF2/Build/Weapon Fx", "Resources/GoF2Weapons from weapon_fx.json: each weapon's projectile, muzzle flash, impact and shot sound, and the crosshair."),
                    T("GoF2/Build/Story Assets", "Resources/GoF2Story/StoryAssets: the story's voice lines (English and German), portrait parts, generic and lounge voices."),
                    T("GoF2/Build/Supernova Assets", "Resources/GoF2Story/SupernovaAssets: the Supernova add-on's sounds, music and the supernova sky."),
                    T("GoF2/Build/Star Map Assets", "Resources/GoF2StarMap: what the star map can't load by name (its overlay, sun materials, Khador effects, sounds)."),
                    T("GoF2/Build/Network Prefabs", "Resources/GoF2Net: the multiplayer prefabs. Existing ones are kept so their network ids stay the same. Also turns on Android's internet permission."),
                    T("GoF2/Build/Linux Dedicated Server", "Build/LinuxServer: a headless Linux server without the game's textures, sound and shaders (Unity's Dedicated Server build; needs the Hub module \"Linux Dedicated Server Build Support\"). Starts as a server by itself; start-server.sh beside it."),
                    T("GoF2/Build/Event Audio", "Resources/GoF2Events/EventAudio: the sounds and music the event graphs (quests, bar missions, multiplayer events) and /sound, /music play, from the original's FMOD events."),
                    T("GoF2/Build/Hangar Shadows", "Resources/GoF2Station/ShipShadows: each ship's soft contact shadow in the hangars (its hull seen from above: silhouette, halo, underside heights) and the projector material. Run after changing a ship model."),
                    T("GoF2/Build/Hangar Heights", "Resources/GoF2Data/hangar_heights.json: how far each ship is lifted on each hangar pad so its hull doesn't cut into it. Run after changing a hangar or a ship model."),
                    T("GoF2/Build/Void Station Collision", "Resources/GoF2Data/void_station_extra.json: collision boxes for the Void station's outer blades and lower spire, which the original's volumes miss. Run after changing the station's model or animation."),
                    T("GoF2/Build/HUD Images", "Resources/GoF2Hud: the HUD, star map, medal and touch-control images and the alien font, cut from the original interface atlases."),
                    T("GoF2/Build/Item Icons", "Resources/GoF2Icons: the shop icon of every item and ship."),
                    T("GoF2/Build/Hull Icons", "Resources/GoF2Icons/hull_*: rendered icons for the hulls with no shop image (freighters, battleships, the carrier, the Valkyrie, the Void ship) in the Debug Ships tab. Build Item Icons first (its ship frame)."),
                    T("GoF2/Build/Text Icons", "The dialogue's inline icons (coin, race emblems, item and ship icons) as one sprite asset. Uses the HUD images and item icons, so build those first."),
                    T("GoF2/Build/Modding AI Reference", "Modding/ai/gof2-modding/reference.md: every original item, ship, system and station with its number and stats, for the AI modding guide (SKILL.md)."),
                    T("GoF2/Build/Sky Layers", "Resources/GoF2Backdrop: the extra sky meshes (planet ring sky, supernova flares, storms, asteroid belt)."),
                    T("GoF2/Build/Space Skies", "Bakes the star and nebula cubemaps the flight levels use (Resources/GoF2Sky). Takes a while."),
                    T("GoF2/Build/Materials And Prefabs", "One material per game material and one prefab per game mesh, from resources.json. Everything else is built on these. Slow.", true),
                    T("GoF2/Build/Assembled Prefabs", "Resources/Assembled: one prefab per game object (ships, stations, gates...), from assemblies.json. Run after build_assemblies.py.", true),
                },
            },
            new Group
            {
                title = "Import",
                about = "The imported game files (Models, Textures, Audio).",
                tools = new[]
                {
                    T("GoF2/Import/Reapply Import Settings", "Reimports every model, texture and sound with the project's import rules (AssetImport). Slow.", true),
                    T("GoF2/Import/Reimport Models Only", "The same for the models only.", true),
                    T("GoF2/Import/Apply Emissive Glow To Materials", "Sets how much each material blooms (_Glow: lights 4, additive layers 2.5, the rest 1)."),
                },
            },
            new Group
            {
                title = "Setup",
                about = "Project setup, run once (already done in this project).",
                tools = new[]
                {
                    T("GoF2/Setup/Install Asset Pack", "Joins the split asset pack in GoF2_ImportParts and extracts it into Assets. Only for a fresh clone.", true),
                    T("GoF2/Setup/Configure VR (OpenXR)", "Sets up OpenXR for PC VR (not started with the game, the controller profiles). Safe to run again."),
                },
            },
            new Group
            {
                title = "Multiplayer events",
                about = "Node graphs for the server's /event game modes (also in the Project window's right-click menu).",
                tools = new[]
                {
                    T("Assets/Create/GoF2/Event Graph", "A new event graph with its Start node in the selected folder."),
                    T("Assets/Create/GoF2/Event Graph From Template/King of the Hill", "A ready event: points for holding an orbit, pirates every 30 s, a scoreboard."),
                    T("Assets/Create/GoF2/Event Graph From Template/Boss Fight", "A ready event: a warlord with escorts, boss music, a reward for the survivors."),
                    T("Assets/Create/GoF2/Event Graph From Template/Free For All", "A ready event: every pilot against every other, a point per kill, respawns in space."),
                    T("Assets/Create/GoF2/Event Graph From Template/Pirate Base", "A ready event: a waypoint to a far pirate base; the boss's question, a wrong answer turns the pirates."),
                    T("Assets/Create/GoF2/Event Graph From Template/Quiz", "A ready event: multiple-choice questions, a point for each right answer."),
                    T("Assets/Create/GoF2/Event Graph From Template/Vote For The Next Event", "A ready event: the players vote, the most picked event starts."),
                    T("Assets/Create/GoF2/Event Graph From Template/Race", "A ready event: the first to dock at a station wins."),
                },
            },
            new Group
            {
                title = "Legacy",
                about = "Old test tools, not used by the game.",
                tools = new[]
                {
                    T("GoF2/Legacy/Flight Test Scene", "Assets/Scenes/FlightTest.unity: a ship and the chase camera (not kept in the repo or the build)."),
                    T("GoF2/Legacy/Bake Combined Skyboxes", "The old combined sky bakes in Assets/Skyboxes, used only by the flight test scene."),
                },
            },
        };

        [MenuItem("GoF2/Tools Overview...", priority = 0)]
        static void Open() => GetWindow<GoF2ToolsWindow>("GoF2 Tools").minSize = new Vector2(460, 300);

        Vector2 scroll;
        string search = "";
        GUIStyle wrap, header;

        void OnGUI()
        {
            wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true };
            header ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Everything these tools make is already in the repo. Run one after changing its builder code or the data it reads.", wrap);
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var g in Groups)
            {
                bool any = false;
                foreach (var t in g.tools) if (Matches(t)) any = true;
                if (!any) continue;
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField(g.title, header);
                EditorGUILayout.LabelField(g.about, EditorStyles.wordWrappedMiniLabel);
                foreach (var t in g.tools)
                {
                    if (!Matches(t)) continue;
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            EditorGUILayout.LabelField(Name(t), EditorStyles.boldLabel);
                            EditorGUILayout.LabelField(t.what, wrap);
                        }
                        if (GUILayout.Button("Run", GUILayout.Width(60), GUILayout.Height(28))) Run(t);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static string Name(Tool t) => t.menu.Substring(t.menu.LastIndexOf('/') + 1);

        bool Matches(Tool t) => string.IsNullOrWhiteSpace(search)
            || t.menu.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || t.what.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        static void Run(Tool t)
        {
            if (t.confirm && !EditorUtility.DisplayDialog(Name(t), t.what + "\n\nRun it now?", "Run", "Cancel")) return;
            // After this frame's GUI: a tool may open dialogs, change scenes or recompile.
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.ExecuteMenuItem(t.menu)) Debug.LogWarning($"GoF2 Tools: no menu item {t.menu}");
            };
        }
    }
}
