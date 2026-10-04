// CombatAudio.cs
// Combat sounds that aren't tied to one weapon (weapons.md section 5): asteroid destroyed (event 21),
// target lock acquired (event 26); and the mining ones (mining.md 4.8): drill loop (1), landing (2), drill off target (3),
// autopilot on / off (28 / 29). Resources/GoF2Weapons/CombatAudio, made by GoF2 > Build > Weapon Fx.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class CombatAudio : ScriptableObject
    {
        public AudioClip asteroidDestroyed;
        public AudioClip targetLock;
        public AudioClip miningDrill;              // 1 Mining_Drill layer 1: Mining_Drill_Add_1 (DrillSound)
        public AudioClip miningDrillSlow, miningDrillAdd2, miningDrillSwitch;   // layers 0 / 2 / 3
        [Tooltip("PlayerEngine: events 42 / 43 / 44 / 45 / 1104 / 1106 / 1107 (their first layer; the extras their second).")]
        public AudioClip[] playerEngines, playerEngineExtras;
        [Tooltip("PlayerEngine: boost events 38-41 (boosters 71-74) and 1102 (195).")]
        public AudioClip[] boosters;
        [Tooltip("The flight UI's buttons: 124 Button_Push / 123 Button_Release, 126 Message_Info_Screen (ChoiceWindow::set).")]
        public AudioClip buttonPush, buttonRelease, messageInfo;
        [Tooltip("97 Button_Info: the hangar's item info window (HangarWindow::OnTouchEnd case 0).")]
        public AudioClip buttonInfo;
        public AudioClip miningLanding;
        public AudioClip miningDrillBroken;
        public AudioClip autopilotOn;
        public AudioClip autopilotOff;
        public AudioClip jumpToPlanet;   // 5 Jump_to_planets (autopilot_travel.md 3.5)

        public static CombatAudio Load() => Resources.Load<CombatAudio>($"{WeaponFx.ResourcesFolder}/CombatAudio");
    }
}
