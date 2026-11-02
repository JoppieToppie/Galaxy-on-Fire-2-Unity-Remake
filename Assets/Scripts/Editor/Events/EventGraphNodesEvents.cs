// EventGraphNodesEvents.cs
// The EventGraph's nodes: event results (Score, Winner, Add Points, Scoreboard) and the commands. The base classes, flow and triggers are in EventGraphNodes.cs.

using System;
using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.EditorTools
{
    // ---- event results -----------------------------------------------------------------------------------------------

    [Serializable]
    [Node("Event", null, "Score")]
    public class EventScoreNode : EventFlowNode
    {
        public const string By = "By";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventScoreBy>(By).WithTooltip("How the winner is decided: the most event ships destroyed, or the longest alive in space since the first spawn.")
                .WithDefaultValue(EventScoreBy.Time).Build();
    }

    [Serializable]
    [Node("Event", null, "Winner")]
    public class EventWinnerNode : EventFlowNode
    {
        public const string By = "By";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventWinnerBy>(By).WithTooltip("Shows \"The winner is X\" to everyone (Score: as the Score step chose).").Build();
    }

    [Serializable]
    [Node("Event", null, "Add Points")]
    public class EventAddPointsNode : EventCommandNode
    {
        public const string Amount = "Amount";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            NumberIn(context, Amount, 1f, "Points added to each of them (negative takes away). Score: Points makes them decide the winner.");
        }
    }

    [Serializable]
    [Node("Event", null, "Scoreboard")]
    public class EventScoreboardNode : EventFlowNode
    {
        public const string Show = "Show", TitleText = "Title";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(Show).WithDefaultValue(true).WithTooltip("On: the scoreboard on every player's screen (by the Score mode, the top 10); off hides it.").Build();

        protected override void DefineInputs(IPortDefinitionContext context) => TextIn(context, TitleText, "", "Its title (empty = Scoreboard).");
    }

    [Serializable]
    [Node("Event", null, "Start Event")]
    public class EventStartEventNode : EventFlowNode
    {
        public const string Event = "Event", Settings = "Settings";
        protected override string[] Exits => Array.Empty<string>();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            TextIn(context, Event, "", "The event to start (its graph's file name: in an Events folder or built in). This event ends here.");
            TextIn(context, Settings, "", "Optional: its settings, \"name=value\" separated by spaces (e.g. waves=10).");
        }
    }

    [Serializable]
    [Node("Event", null, "Free For All")]
    public class EventFreeForAllNode : EventFlowNode
    {
        public const string On = "On";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(On).WithDefaultValue(true).WithTooltip("On: every pilot is every other's enemy (squadmates excepted) until off or the event's end.").Build();
    }

    [Serializable]
    [Node("Event", null, "Set Respawn Point")]
    public class EventSetRespawnNode : EventCommandNode
    {
        public const string OrbitIn = "Orbit", At = "At", Spread = "Spread", Delay = "Delay";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("Where their ships come back after being destroyed (wire Get Orbit in), instead of docked at a station.").Build();
            TextIn(context, At, "", "Game coordinates \"x y z\" in that orbit (/pos shows yours); empty = 3 km in front of the station.");
            NumberIn(context, Spread, 2000f, "Each comes back up to this far from the spot (game units; 2000 = 100 m), so they don't stack.");
            NumberIn(context, Delay, 5f, "Seconds after being destroyed (at least 3.5: the explosion).");
        }
    }

    [Serializable]
    [Node("Event", null, "Make Hostile")]
    public class EventMakeHostileNode : EventCommandNode
    {
        public const string Race = "Race", Within = "Within";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventRace>(Race).WithTooltip("Only ships of this race (Maker = every race).").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            NumberIn(context, Within, 0f, "Only ships within this many metres of each player (0 = the whole orbit). They turn on that player and " +
                "their squad, like ships they shot (neutral spawns too).");
        }
    }

    [Serializable]
    [Node("Event", null, "Set Waypoint")]
    public class EventSetWaypointNode : EventCommandNode
    {
        public const string OrbitIn = "Orbit", At = "At";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("The waypoint's orbit (wire Get Orbit in).").Build();
            TextIn(context, At, "", "Game coordinates \"x y z\" (/pos shows yours): a lockable Waypoint the autopilot flies to; reaching it clears it.");
        }
    }

    [Serializable]
    [Node("Event", null, "Open Wormhole")]
    public class EventOpenWormholeNode : EventCommandNode
    {
        public const string OrbitIn = "Orbit", At = "At", Destination = "Destination", Seconds = "Seconds";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("The orbit the wormhole opens in (wire Get Orbit in).").Build();
            TextIn(context, At, "", "Game coordinates \"x y z\" in that orbit (/pos shows yours); empty = 4 km out, beside the launch path. Keep it 2 km (40000) clear of the line straight ahead of the station (x 0, y 0): launching ships fly along it.");
            context.AddInputPort<Orbit>(Destination).WithTooltip("Where flying in takes them (wire Get Orbit in: a station, \"void\", or a " +
                "wired number such as a variable). A stream-out arrival there, with a wormhole closing behind.").Build();
            NumberIn(context, Seconds, 0f, "Open this many seconds (0 = until Close Wormhole or the event's end). One per player: a new one replaces it.");
        }
    }

    [Serializable]
    [Node("Event", null, "Close Wormhole")]
    public class EventCloseWormholeNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Event", null, "Show / Hide System")]
    public class EventSystemNode : EventCommandNode
    {
        public const string SystemIn = "System", Action = "Action";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventSystemAction>(Action).WithTooltip("Show: on the star map, its routes and jumps too, whatever " +
                "was revealed. Hide: off it; a player in it is taken out to the nearest system on the map. Reset: as the map " +
                "has it again. Kept (and saved) until changed.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, SystemIn, "", "The system: its number, its name or a mod's key \"mod_id:id\".");
        }
    }

    [Serializable]
    [Node("Event", null, "Clear Waypoint")]
    public class EventClearWaypointNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Event", null, "Restrict Travel")]
    public class EventRestrictTravelNode : EventCommandNode
    {
        public const string NoJumps = "NoJumps", NoDocking = "NoDocking", NoKhador = "NoKhador";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<bool>(NoJumps).WithDisplayName("No jumps").WithDefaultValue(true).WithTooltip("No planet jumps, jumpgate or Khador Drive (an admin's teleport still works).").Build();
            context.AddOption<bool>(NoDocking).WithDisplayName("No docking").WithDefaultValue(true).WithTooltip("No docking at a station.").Build();
            context.AddOption<bool>(NoKhador).WithDisplayName("No Khador Drive").WithDefaultValue(false)
                .WithTooltip("With No jumps off: only the Khador Drive is refused (the gates and planet jumps still work), like the Valkyrie's last mission.").Build();
        }

        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Event", null, "Set Flight Model")]
    public class EventFlightModelNode : EventCommandNode
    {
        public const string Model = "Model";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventFlightModel>(Model).WithTooltip("Free flight (EVERSPACE 2 style) or the original's flight for " +
                "these players, before their own option, their ship's or the mod campaign's; Players Choice gives it back. Undone " +
                "when the event ends.").Build();

        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Event", null, "Clear Respawn Point")]
    public class EventClearRespawnNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    // ---- commands ----------------------------------------------------------------------------------------------------

    [Serializable]
    [Node("Commands", null, "Title")]
    public class EventTitleNode : EventCommandNode
    {
        public const string TitleText = "Title", SubtitleText = "Subtitle", Seconds = "Seconds";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, TitleText, "", "The big title ({expression} parts are filled in). Title and subtitle empty = clear the title.");
            TextIn(context, SubtitleText, "", "The line under it.");
            NumberIn(context, Seconds, 0f, "How long it shows (0 = 4 s).");
        }
    }

    [Serializable]
    [Node("Commands", null, "Timer")]
    public class EventTimerNode : EventCommandNode
    {
        public const string Seconds = "Seconds", Label = "Label";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            NumberIn(context, Seconds, 60f, "The countdown at the top of the screen (the last 10 s amber).");
            TextIn(context, Label, "", "The text beside it.");
        }
    }

    [Serializable]
    [Node("Commands", null, "Stop Timer")]
    public class EventStopTimerNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Spawn")]
    public class EventSpawnNode : EventCommandNode
    {
        public const string Ship = "Ship", Count = "Count", At = "At", Race = "Race", Behaviour = "Behaviour", Name = "Name", Hull = "Hull", Guns = "Guns";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<EventRace>(Race).WithTooltip("Maker: the ship maker's race (pirates for other makers).").Build();
            context.AddOption<EventBehaviour>(Behaviour).WithTooltip("Enemy (counts as \"enemies\"), friendly, neutral (neither side until shot), or by the standings.").Build();
        }

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Ship, "Hiro", "A ship's number or name (or an object's name from assemblies.json, as scenery; \"wreck\": a hackable " +
                "freighter wreck of the Race (Terran, Vossk, Nivelian, Midorian; else rolled), which fires On Wreck Hacked).");
            NumberIn(context, Count, 1f, "Ships side by side, per player (1..10).");
            TextIn(context, Name, "", "Optional: the name on the HUD (a ship's lock plate, numbered \"Name 1\", \"Name 2\"... for several; " +
                "an object gets a marker with it), at most 32 characters. Empty = the usual name.");
            TextIn(context, At, "", "Empty = ahead of each player; else game coordinates \"x y z\" in their orbit.");
            NumberIn(context, Hull, 1f, "Their hull x this (1 = as usual, 2 = twice as tough; 0.1..50).");
            NumberIn(context, Guns, 1f, "Their gun damage x this (1 = as usual; 0.1..50).");
        }
    }

    /// <summary>Remake: /spawn type: an NPC ship type of npcships.json (the pirate boss among the game's).</summary>
    [Serializable]
    [Node("Commands", null, "Spawn NPC Type")]
    public class EventSpawnTypeNode : EventCommandNode
    {
        public const string Type = "Type", At = "At", Name = "Name";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Type, "pirate_boss", "battleship, carrier, vossk_battleship, freighter, pirate_boss, or a mod's NPC ship \"mod_id:id\": " +
                "its own hull, guns, escorts and turrets.");
            TextIn(context, Name, "", "Optional: the name on its lock plate (the ship orders use it).");
            TextIn(context, At, "", "Empty = 3.5 km ahead of each player; else game coordinates \"x y z\" in their orbit.");
        }
    }

    [Serializable]
    [Node("Commands", null, "Reward")]
    public class EventRewardNode : EventCommandNode
    {
        public const string Credits = "Credits", Items = "Items", TitleText = "Title";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context, "@survivors");
            NumberIn(context, Credits, 1000f, "Credits paid (0 = none).");
            TextIn(context, Items, "", "Items into the hold: \"<item> [amount]\", several joined by +, e.g. \"Energy Cells 10 + Gold 5\".");
            TextIn(context, TitleText, "", "The reward box's title (empty = \"Mission accomplished!\").");
        }
    }

    /// <summary>A command that takes only the players.</summary>
    [Serializable]
    public abstract class EventPlayersNode : EventCommandNode
    {
        protected abstract string Word { get; }
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable] [Node("Commands", null, "Heal")] public class EventHealNode : EventPlayersNode { protected override string Word => "heal"; }
    [Serializable] [Node("Commands", null, "Refill Ammo")] public class EventAmmoNode : EventPlayersNode { protected override string Word => "ammo"; }
    [Serializable] [Node("Commands", null, "Destroy Ship")] public class EventKillNode : EventPlayersNode { protected override string Word => "kill"; }
    [Serializable] [Node("Commands", null, "Reveal Map")] public class EventRevealNode : EventPlayersNode { protected override string Word => "reveal"; }
    [Serializable] [Node("Commands", null, "Make Peace")] public class EventPeaceNode : EventPlayersNode { protected override string Word => "peace"; }

    [Serializable]
    [Node("Commands", null, "Give Item")]
    public class EventGiveNode : EventCommandNode
    {
        public const string Item = "Item", Amount = "Amount", Mount = "Mount";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(Mount).WithTooltip("Docked players get it mounted.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Item, "", "An item's number or name.");
            NumberIn(context, Amount, 1f, "1..1000.");
        }
    }

    /// <summary>Remake: /take (Cheats.TakeItem): items out of the hold, a mission's hand-in.</summary>
    [Serializable]
    [Node("Commands", null, "Take Item")]
    public class EventTakeNode : EventCommandNode
    {
        public const string Item = "Item", Amount = "Amount", Mounted = "Mounted";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(Mounted).WithTooltip("Mounted ones too (a Khador Drive, a gun), after the hold's.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Item, "", "An item's number or name.");
            NumberIn(context, Amount, 0f, "How many (0 = all of them).");
        }
    }

    /// <summary>Remake: /standing (NetAdminStanding): the standing toward a game race or a mod's race.</summary>
    [Serializable]
    [Node("Commands", null, "Standing")]
    public class EventStandingNode : EventCommandNode
    {
        public const string Side = "Toward", Amount = "Amount", Set = "Set";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(Set).WithTooltip("Set the standing to the amount instead of moving it.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Side, "terran", "terran, vossk, nivelian, midorian, or a mod's race (its id or mod_id:id).");
            NumberIn(context, Amount, 10f, "-200..200 (below 0: liked less); the standing stays within -100..100.");
        }
    }

    [Serializable]
    [Node("Commands", null, "Credits")]
    public class EventCreditsNode : EventCommandNode
    {
        public const string Amount = "Amount";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            NumberIn(context, Amount, 1000f, "Credits given (negative takes, never below 0).");
        }
    }

    [Serializable]
    [Node("Commands", null, "Teleport")]
    public class EventTeleportNode : EventCommandNode
    {
        public const string Destination = "Destination";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Destination, "", "A player, or a station (number, name or \"void\") with optional game coordinates \"x y z\" or \"dock\".");
        }
    }

    [Serializable]
    [Node("Commands", null, "Change Ship")]
    public class EventShipNode : EventCommandNode
    {
        public const string Ship = "Ship";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Ship, "own", "Any hull's number or name (the debug Ships tab's), or \"own\" for the player's own ship.");
        }
    }

    [Serializable]
    [Node("Commands", null, "Cheat")]
    public class EventCheatNode : EventCommandNode
    {
        public const string Flag = "Flag", On = "On";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<EventCheat>(Flag).Build();
            context.AddOption<bool>(On).WithDefaultValue(true).Build();
        }

        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Radio")]
    public class EventRadioNode : EventCommandNode
    {
        public const string Speaker = "Speaker", Text = "Text";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Speaker, "pirate Boss", "Who calls (a story character like Keith, a mod's character like \"frontier_systems:vega\", a race and a name like \"vossk K'ekki\", or player).");
            TextIn(context, Text, "", "The call: in the flight HUD's radio box with the face, gone by itself (docked: a chat line). %player% = the reader; " +
                "\"[voice <clip>]\" plays its voice-over (an original line, or a mod's voices/<clip>.ogg).");
        }
    }

    [Serializable]
    [Node("Commands", null, "Chat")]
    public class EventChatNode : EventCommandNode
    {
        public const string Text = "Text";

        protected override void DefineInputs(IPortDefinitionContext context) => TextIn(context, Text, "", "One chat line to everyone (Global), from \"Server\".");
    }

    [Serializable]
    [Node("Commands", null, "Dialog")]
    public class EventDialogNode : EventCommandNode
    {
        public const string Pages = "Pages";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<string>(Pages).WithTooltip("One page per line: \"speaker : text\" (a story speaker, a mod's character, \"vossk K'ekki\", \"terran female Jane\" or \"player\"; " +
                "a line without a speaker keeps the last one); \"reward [title]: <rewards>\" pays when it closes; %player% = the reader's name; " +
                "\"[voice <clip>]\" in a page plays its voice-over: an original voice line by its name, or a mod's voices/<clip>.ogg (.wav, .mp3; " +
                "voices/de/<clip>.ogg for the German voices).")
                .AsTextArea(3, 12).Delayed().Build();

        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }
}
