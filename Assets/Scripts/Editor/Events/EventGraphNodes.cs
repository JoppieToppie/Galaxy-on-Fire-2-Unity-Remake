// EventGraphNodes.cs
// The EventGraph's nodes. Steps (on the white flow arrows): Flow (Start, Wait, Wait Until, Wait Forever, If, While, Repeat,
// Run In Parallel, Every, Random Branch, Set Variable, Stop, Comment), Triggers (On: a flow of its own each time something
// happens), Event (Score, Winner, Add Points, Scoreboard) and Commands (the server commands an event uses, one node each,
// Play Sound / Music, and Command for any other line). Values (on the data wires): Game State, Math, Random, Floor, Compare,
// Logic, Not, Expression (raw text), Get Orbit, Get Players, Count Players, plus the blackboard's variables. These classes only define the ports and options: what each node means is in the game's
// EventGraphScript (one compiler for the Editor and the server), which knows them by class, port and option name, and the
// option enums are the game's (GoF2Remake.Multiplayer). Those names are saved in the graph files: renaming one breaks the
// graphs that use it (and EventGraphScript must follow).

using System;
using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    // ---- base classes ------------------------------------------------------------------------------------------------

    [Serializable]
    public abstract class EventNodeBase : Node
    {

        protected static void NumberIn(IPortDefinitionContext c, string name, float value, string tooltip) =>
            c.AddInputPort<float>(name).WithDefaultValue(value).WithTooltip(tooltip).Build();

        protected static void ConditionIn(IPortDefinitionContext c, string name, string tooltip) =>
            c.AddInputPort<bool>(name).WithTooltip(tooltip).Build();

        protected static void TextIn(IPortDefinitionContext c, string name, string value, string tooltip) =>
            c.AddInputPort<string>(name).WithDefaultValue(value).WithTooltip(tooltip).Build();
    }

    /// <summary>A step of the script: the flow arrow in, its exits out (Next and any branches).</summary>
    [Serializable]
    public abstract class EventFlowNode : EventNodeBase
    {
        public const string In = "In", Next = "Next";

        protected virtual string[] Exits => new[] { Next };
        protected virtual bool HasIn => true;

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            if (HasIn) context.AddInputPort<Flow>(In).WithDisplayName("").WithConnectorUI(PortConnectorUI.Arrowhead).Build();
            foreach (string exit in Exits)
                context.AddOutputPort<Flow>(exit).WithDisplayName(exit == Next ? "" : exit).WithConnectorUI(PortConnectorUI.Arrowhead)
                    .WithCapacity(PortCapacity.Single).Build();
            DefineInputs(context);
        }

        protected virtual void DefineInputs(IPortDefinitionContext context) { }
    }

    /// <summary>A server command step: "&lt;word&gt; [players] ...".</summary>
    [Serializable]
    public abstract class EventCommandNode : EventFlowNode
    {
        public const string Players = "Players";

        protected static void PlayersIn(IPortDefinitionContext c, string value = "@a") =>
            TextIn(c, Players, value, "Who: @a everyone, @r a random player, @alive, @space, @docked, @dead, @survivors (never destroyed since the fight began), or a player's name.");
    }

    /// <summary>A value: an expression for the data wires.</summary>
    [Serializable]
    public abstract class EventValueNode : EventNodeBase
    {
        public const string Value = "Value", Result = "Result";
    }

    // ---- flow --------------------------------------------------------------------------------------------------------

    [Serializable]
    [Node("Flow", null, "Start")]
    public class EventStartNode : EventFlowNode
    {
        public const string Description = "Description", MissionTitle = "MissionTitle", MissionOffer = "MissionOffer",
            MissionClient = "MissionClient", MissionReward = "MissionReward", MissionStations = "MissionStations",
            MinPlayers = "MinPlayers", MaxPlayers = "MaxPlayers";
        protected override bool HasIn => false;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(Description).WithTooltip("What the event is (written as # lines at the top of the script).")
                .AsTextArea(2, 8).Delayed().Build();
            context.AddOption<string>(MissionTitle).WithDisplayName("Mission title").WithTooltip("Set it to offer this event as a bar " +
                "mission: a visitor in the multiplayer Space Lounge offers it, and it runs for the squad that takes it (its selectors, " +
                "counts, triggers and scoreboard cover only them). Empty: only /event starts it, for everyone.").Delayed().Build();
            context.AddOption<string>(MissionOffer).WithDisplayName("Offer text").WithTooltip("What the visitor says about the job.")
                .AsTextArea(2, 8).Delayed().Build();
            context.AddOption<string>(MissionClient).WithDisplayName("Client").WithTooltip("Who offers it: a story character (Keith, " +
                "\"Keith as Bob\"), or a race and a name (\"vossk K'ekki\", \"terran female Jane\"). Empty: someone of the station's race.")
                .Delayed().Build();
            context.AddOption<float>(MissionReward).WithDisplayName("Reward").WithTooltip("Credits for Mission Complete, split evenly " +
                "across the team.").Delayed().Build();
            context.AddOption<string>(MissionStations).WithDisplayName("Offered at").WithTooltip("Stations whose bars offer it " +
                "(numbers or names, commas between). Empty: every station.").Delayed().Build();
            context.AddOption<float>(MinPlayers).WithDisplayName("Min pilots").WithDefaultValue(1f).WithTooltip("The squad needs at least this many pilots.").Delayed().Build();
            context.AddOption<float>(MaxPlayers).WithDisplayName("Max pilots").WithDefaultValue(4f).WithTooltip("And at most this many.").Delayed().Build();
        }
    }

    [Serializable]
    [Node("Event", null, "Mission Complete")]
    public class EventMissionCompleteNode : EventFlowNode
    {
        public const string Reward = "Reward", TitleText = "Title";
        protected override string[] Exits => Array.Empty<string>();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            NumberIn(context, Reward, -1f, "Credits split evenly across the team (-1 = the Start node's mission reward).");
            TextIn(context, TitleText, "", "The reward box's title (empty: \"Mission accomplished!\").");
        }
    }

    [Serializable]
    [Node("Event", null, "Mission Failed")]
    public class EventMissionFailedNode : EventFlowNode
    {
        public const string TitleText = "Title";
        protected override string[] Exits => Array.Empty<string>();

        protected override void DefineInputs(IPortDefinitionContext context) =>
            TextIn(context, TitleText, "", "The title on the team's screens (empty: \"Mission failed!\"). The mission ends.");
    }

    [Serializable]
    [Node("Flow", null, "Comment")]
    public class EventCommentNode : EventFlowNode
    {
        public const string Text = "Text";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<string>(Text).WithTooltip("A note in the script (# lines); does nothing.").AsTextArea(1, 6).Delayed().Build();
    }

    [Serializable]
    [Node("Flow", null, "Wait")]
    public class EventWaitNode : EventFlowNode
    {
        public const string Seconds = "Seconds";

        protected override void DefineInputs(IPortDefinitionContext context) => NumberIn(context, Seconds, 1f, "How long the script pauses.");
    }

    [Serializable]
    [Node("Flow", null, "Wait Until")]
    public class EventWaitUntilNode : EventFlowNode
    {
        public const string Condition = "Condition", Timeout = "Timeout";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            ConditionIn(context, Condition, "The script goes on once this holds (checked 4 times a second).");
            NumberIn(context, Timeout, 0f, "Seconds after which it goes on anyway (0 = no limit).");
        }
    }

    [Serializable]
    [Node("Flow", null, "If")]
    public class EventIfNode : EventFlowNode
    {
        public const string Condition = "Condition", Then = "Then", Else = "Else";
        protected override string[] Exits => new[] { Then, Else, Next };

        protected override void DefineInputs(IPortDefinitionContext context) => ConditionIn(context, Condition, "Then runs when this holds, else Else; then Next.");
    }

    [Serializable]
    [Node("Flow", null, "While")]
    public class EventWhileNode : EventFlowNode
    {
        public const string Condition = "Condition", Body = "Body";
        protected override string[] Exits => new[] { Body, Next };

        protected override void DefineInputs(IPortDefinitionContext context) => ConditionIn(context, Condition, "Body runs again and again while this holds; then Next.");
    }

    [Serializable]
    [Node("Flow", null, "Repeat")]
    public class EventRepeatNode : EventFlowNode
    {
        public const string Count = "Count", Body = "Body", Counter = "Counter";
        protected override string[] Exits => new[] { Body, Next };

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<string>(Counter).WithTooltip("Optional: a variable that counts the runs 1, 2, 3... (add it to the blackboard to read it).")
                .Delayed().Build();

        protected override void DefineInputs(IPortDefinitionContext context) => NumberIn(context, Count, 3f, "How many times Body runs; then Next.");
    }

    [Serializable]
    [Node("Flow", null, "Set Variable")]
    public class EventSetNode : EventFlowNode
    {
        public const string Variable = "Variable", Value = "Value";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<string>(Variable).WithTooltip("The blackboard variable to set.").Delayed().Build();

        protected override void DefineInputs(IPortDefinitionContext context) => NumberIn(context, Value, 0f, "The new value.");
    }

    [Serializable]
    [Node("Flow", null, "Wait Forever")]
    public class EventWaitForeverNode : EventFlowNode
    {
        protected override string[] Exits => Array.Empty<string>();
    }

    [Serializable]
    [Node("Flow", null, "Run In Parallel")]
    public class EventParallelNode : EventFlowNode
    {
        public const string Branch = "Branch";
        protected override string[] Exits => new[] { Branch, Next };
    }

    [Serializable]
    [Node("Flow", null, "Every")]
    public class EventEveryNode : EventFlowNode
    {
        public const string Seconds = "Seconds", Body = "Body";
        protected override string[] Exits => new[] { Body, Next };

        protected override void DefineInputs(IPortDefinitionContext context) =>
            NumberIn(context, Seconds, 10f, "Body runs every this many seconds (first after one wait) until the event ends; Next goes on at once.");
    }

    [Serializable]
    [Node("Flow", null, "Random Branch")]
    public class EventRandomBranchNode : EventFlowNode
    {
        protected override string[] Exits => new[] { "Option 1", "Option 2", "Option 3", "Option 4", Next };

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            for (int k = 1; k <= 4; k++) NumberIn(context, "Weight " + k, 1f, "How likely Option " + k + " is against the others (whole numbers); unwired options don't count.");
        }
    }

    [Serializable]
    [Node("Flow", null, "Stop")]
    public class EventStopNode : EventFlowNode
    {
        protected override string[] Exits => Array.Empty<string>();
    }

    [Serializable]
    [Node("Flow", null, "Ask")]
    public class EventAskNode : EventFlowNode
    {
        public const string Question = "Question", Seconds = "Seconds", PlayerOut = "Player", NameOut = "Name";
        protected override string[] Exits => new[] { "Answer 1", "Answer 2", "Answer 3", "Answer 4", Next };

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            TextIn(context, EventCommandNode.Players, "@a", "Who is asked (a selector, or wire Get Players in).");
            TextIn(context, "Speaker", "", "Optional: who asks, with a portrait (a story character like Keith, a race and a name like \"vossk K'ekki\", or player).");
            TextIn(context, Question, "", "The question on their screens (%player% = the reader's name).");
            for (int k = 1; k <= 4; k++)
                TextIn(context, "Answer " + k, "", k <= 2 ? "An answer (2 at least)." : "Optional: another answer.");
            NumberIn(context, Seconds, 20f, "How long they have (0 = no limit; Esc skips). Next goes on when all have answered or the time is up.");
            context.AddOutputPort<Players>(PlayerOut).WithTooltip("In an answer's branch: the player who picked it (wire into Players).").Build();
            context.AddOutputPort<string>(NameOut).WithTooltip("In an answer's branch: that player's name; or type %trigger% in a text.").Build();
        }
    }

    [Serializable]
    [Node("Flow", null, "Vote")]
    public class EventVoteNode : EventFlowNode
    {
        public const string Question = "Question", Seconds = "Seconds";
        protected override string[] Exits => new[] { "Choice 1", "Choice 2", "Choice 3", "Choice 4", "No Votes", Next };

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            TextIn(context, EventCommandNode.Players, "@a", "Who votes (a selector, or wire Get Players in).");
            TextIn(context, "Speaker", "", "Optional: who asks, with a portrait (a story character like Keith, a race and a name like \"vossk K'ekki\", or player).");
            TextIn(context, Question, "", "The question on their screens (%player% = the reader's name).");
            for (int k = 1; k <= 4; k++)
                TextIn(context, "Answer " + k, "", k <= 2 ? "An answer (2 at least)." : "Optional: another answer.");
            NumberIn(context, Seconds, 20f, "How long the vote lasts (0 = until all have voted). Then the most picked answer's Choice runs " +
                "(a tie: one of them at random; nobody voted: No Votes), then Next.");
        }
    }

    // ---- triggers ----------------------------------------------------------------------------------------------------

    [Serializable]
    [Node("Triggers", null, "On")]
    public class EventOnNode : EventFlowNode
    {
        public const string Trigger = "Trigger", OrbitIn = "Orbit", Do = "Do", PlayerOut = "Player", NameOut = "Name", VictimOut = "Victim";
        protected override bool HasIn => false;
        protected override string[] Exits => new[] { Do };

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventTrigger>(Trigger).WithTooltip("What starts Do (a flow of its own, each time, while the event runs): a player destroyed, " +
                "docked, launched, joined or left the session, entered an orbit, destroyed one of the event's ships, destroyed another player (Player = the " +
                "killer, Victim = the other's name), came back after being destroyed, or every enemy the event spawned is gone.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("Optional: only when it happens in this orbit (or at its station). Player Arrives needs it.").Build();
            TextIn(context, "At", "", "Player Arrives: the spot's game coordinates \"x y z\" in that orbit (/pos shows yours).");
            NumberIn(context, "Radius", 1000f, "Player Arrives: how close (metres) a player alive in space must come; again after leaving it.");
            context.AddOutputPort<Players>(PlayerOut).WithTooltip("The player it happened to (the killer for Event Ship Destroyed): wire into a command's Players.").Build();
            context.AddOutputPort<string>(NameOut).WithTooltip("That player's name: wire into a text (\"X was destroyed\"); or type %trigger% in a text.").Build();
            context.AddOutputPort<string>(VictimOut).WithTooltip("Player Killed Player: the destroyed player's name; or type %victim% in a text.").Build();
        }
    }

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
    [Node("Event", null, "Clear Waypoint")]
    public class EventClearWaypointNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Event", null, "Restrict Travel")]
    public class EventRestrictTravelNode : EventCommandNode
    {
        public const string NoJumps = "NoJumps", NoDocking = "NoDocking";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<bool>(NoJumps).WithDisplayName("No jumps").WithDefaultValue(true).WithTooltip("No planet jumps, jumpgate or Khador Drive (an admin's teleport still works).").Build();
            context.AddOption<bool>(NoDocking).WithDisplayName("No docking").WithDefaultValue(true).WithTooltip("No docking at a station.").Build();
        }

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
        public const string Ship = "Ship", Count = "Count", At = "At", Race = "Race", Behaviour = "Behaviour", Name = "Name";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<EventRace>(Race).WithTooltip("Maker: the ship maker's race (pirates for other makers).").Build();
            context.AddOption<EventBehaviour>(Behaviour).WithTooltip("Enemy (counts as \"enemies\"), friendly, neutral (neither side until shot), or by the standings.").Build();
        }

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, Ship, "Hiro", "A ship's number or name (or an object's name from assemblies.json, as scenery).");
            NumberIn(context, Count, 1f, "Ships side by side, per player (1..10).");
            TextIn(context, Name, "", "Optional: the name on the HUD (a ship's lock plate, numbered \"Name 1\", \"Name 2\"... for several; " +
                "an object gets a marker with it), at most 32 characters. Empty = the usual name.");
            TextIn(context, At, "", "Empty = ahead of each player; else game coordinates \"x y z\" in their orbit.");
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
            TextIn(context, Speaker, "pirate Boss", "Who calls (a story character like Keith, a race and a name like \"vossk K'ekki\", or player).");
            TextIn(context, Text, "", "The call: in the flight HUD's radio box with the face, gone by itself (docked: a chat line). %player% = the reader.");
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
            context.AddOption<string>(Pages).WithTooltip("One page per line: \"speaker : text\" (a story speaker, \"vossk K'ekki\", \"terran female Jane\" or \"player\"; " +
                "a line without a speaker keeps the last one); \"reward [title]: <rewards>\" pays when it closes; %player% = the reader's name.")
                .AsTextArea(3, 12).Delayed().Build();

        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Play Sound")]
    public class EventPlaySoundNode : EventCommandNode
    {
        public const string Sound = "Sound";
        protected override void OnDefineOptions(IOptionDefinitionContext context) => context.AddOption<EventSound>(Sound).Build();
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Play Music")]
    public class EventPlayMusicNode : EventCommandNode
    {
        public const string Music = "Music";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventMusic>(Music).WithTooltip("Looped instead of the game's music until Stop Music or the event's end.").Build();

        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Stop Music")]
    public class EventStopMusicNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Command")]
    public class EventRawCommandNode : EventCommandNode
    {
        public const string Line = "Line";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<string>(Line).WithTooltip("Any server command without the \"/\" (/help lists them); {expression} parts are filled in.")
                .Delayed().Build();
    }

    // ---- players -----------------------------------------------------------------------------------------------------

    [Serializable]
    [Node("Players", null, "Get Orbit")]
    public class EventGetOrbitNode : EventValueNode
    {
        public const string Station = "Station", OrbitOut = "Orbit";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TextIn(context, Station, "78", "The station whose orbit: its number, its name (\"Var Hastra\") or \"void\"; or wire a number in.");
            context.AddOutputPort<Orbit>(OrbitOut).Build();
        }
    }

    [Serializable]
    [Node("Players", null, "Get Players")]
    public class EventGetPlayersNode : EventValueNode
    {
        public const string Who = "Who", OrbitIn = "Orbit", PlayersOut = "Players";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventPlayersWho>(Who).WithTooltip("Everyone, alive in space, in space, docked, destroyed in space, the event's survivors (never " +
                "destroyed since its fight began), or one random player.").Build();

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("Optional: only the players in this orbit (or docked at its station).").Build();
            context.AddOutputPort<Players>(PlayersOut).WithTooltip("Wire into a command's Players.").Build();
        }
    }

    [Serializable]
    [Node("Players", null, "Count Players")]
    public class EventCountPlayersNode : EventValueNode
    {
        public const string PlayersIn = "Players";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TextIn(context, PlayersIn, "@alive", "Whom to count: wire Get Players (or an On node's Player) in, or type a selector.");
            context.AddOutputPort<float>(Value).Build();
        }
    }

    // ---- values ------------------------------------------------------------------------------------------------------

    [Serializable]
    [Node("Values", null, "Game State")]
    public class EventStateNode : EventValueNode
    {
        public const string State = "State";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventGameValue>(State).WithTooltip("Enemies / Ships: this event's living spawned ships (enemies only / all); Players: in the session; " +
                "In Space: alive in space; Docked; Dead: destroyed in space; Time: seconds since the event started; Top Points; Mission Station: " +
                "where a bar mission was taken (-1 in an /event; wire it into Get Orbit).").Build();

        protected override void OnDefinePorts(IPortDefinitionContext context) => context.AddOutputPort<float>(Value).Build();
    }

    [Serializable]
    [Node("Values", null, "Math")]
    public class EventMathNode : EventValueNode
    {
        public const string A = "A", B = "B", Op = "Op";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventMathOp>(Op).WithTooltip("Divide / Modulo by 0 give 0.").Build();

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(A).Build();
            context.AddInputPort<float>(B).Build();
            context.AddOutputPort<float>(Value).Build();
        }
    }

    [Serializable]
    [Node("Values", null, "Random")]
    public class EventRandomNode : EventValueNode
    {
        public const string Min = "Min", Max = "Max";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            NumberIn(context, Min, 1f, "Whole numbers from Min to Max, both included.");
            NumberIn(context, Max, 6f, "");
            context.AddOutputPort<float>(Value).Build();
        }
    }

    [Serializable]
    [Node("Values", null, "Floor")]
    public class EventFloorNode : EventValueNode
    {
        public const string Input = "Input";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(Input).Build();
            context.AddOutputPort<float>(Value).Build();
        }
    }

    [Serializable]
    [Node("Values", null, "Compare")]
    public class EventCompareNode : EventValueNode
    {
        public const string A = "A", B = "B", Op = "Op";

        protected override void OnDefineOptions(IOptionDefinitionContext context) => context.AddOption<EventCompareOp>(Op).Build();

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<float>(A).Build();
            context.AddInputPort<float>(B).Build();
            context.AddOutputPort<bool>(Result).Build();
        }
    }

    [Serializable]
    [Node("Values", null, "Logic")]
    public class EventLogicNode : EventValueNode
    {
        public const string A = "A", B = "B", Op = "Op";

        protected override void OnDefineOptions(IOptionDefinitionContext context) => context.AddOption<EventLogicOp>(Op).Build();

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<bool>(A).Build();
            context.AddInputPort<bool>(B).Build();
            context.AddOutputPort<bool>(Result).Build();
        }
    }

    [Serializable]
    [Node("Values", null, "Not")]
    public class EventNotNode : EventValueNode
    {
        public const string Input = "Input";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<bool>(Input).Build();
            context.AddOutputPort<bool>(Result).Build();
        }
    }

    [Serializable]
    [Node("Values", null, "Expression")]
    public class EventExpressionNode : EventValueNode
    {
        public const string Text = "Text", Number = "Number", Condition = "Condition";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<string>(Text).WithTooltip("Any script expression: numbers, variables, game values, + - * / %, comparisons, and / or / not, random(a, b), min, max, floor.")
                .WithDefaultValue("0").Delayed().Build();

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<float>(Number).Build();
            context.AddOutputPort<bool>(Condition).Build();
        }
    }
}
