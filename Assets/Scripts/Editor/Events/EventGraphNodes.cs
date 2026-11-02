// EventGraphNodes.cs
// The EventGraph's nodes. Steps (on the white flow arrows): Flow (Start, Wait, Wait Until, Wait Forever, If, While, Repeat,
// Run In Parallel, Every, Random Branch, Set Variable, Stop, Comment), Triggers (On: a flow of its own each time something
// happens), Event (Score, Winner, Add Points, Scoreboard) and Commands (the server commands an event uses, one node each,
// Play Sound / Music, and Command for any other line). Values (on the data wires): Game State, Math, Random, Floor, Compare,
// Logic, Not, Expression (raw text), Get Orbit, Get Players, Count Players, plus the blackboard's variables. These classes only define the ports and options: what each node means is in the game's
// EventGraphScript (one compiler for the Editor and the server), which knows them by class, port and option name, and the
// option enums are the game's (GoF2Remake.Multiplayer). Those names are saved in the graph files: renaming one breaks the
// graphs that use it (and EventGraphScript must follow).
// This file holds the base classes, flow and triggers; EventGraphNodesEvents, EventGraphNodesScenes and
// EventGraphNodesValues the rest.

using System;
using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using GoF2Remake.Events;

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
            MinPlayers = "MinPlayers", MaxPlayers = "MaxPlayers", Kind = "Kind", StartsWhen = "StartsWhen", MissionChance = "MissionChance",
            MissionTakers = "MissionTakers", RepeatMinutes = "RepeatMinutes", OnlyOnce = "OnlyOnce";
        protected override bool HasIn => false;

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(Description).WithTooltip("What the event is (written as # lines at the top of the script).")
                .AsTextArea(2, 8).Delayed().Build();
            context.AddOption<EventKind>(Kind).WithTooltip("Event: started by /event (with a Mission title it is offered as a bar mission, as " +
                "before). Bar Mission: offered by a Space Lounge visitor (multiplayer squads and single player). Story Mission: single player, like the " +
                "story: starts by itself when Starts when holds, lasts across saves (Checkpoint), shows its Mission title and objective in the " +
                "Missions window; Mission Complete finishes it for good, Mission Failed loads the last save.").Build();
            context.AddOption<string>(StartsWhen).WithDisplayName("Starts when").WithTooltip("A story mission: the condition that starts it, checked " +
                "every second in a game (an expression: \"campaign >= 20\", \"visited(Kepler Prime)\", \"quest(first_contact) == 2\", " +
                "\"credits > 50000\"). Empty: at once; \"never\": only by another graph's Start Mission. A bar mission (single player): " +
                "no bar offers it until it holds.").Delayed().Build();
            context.AddOption<bool>(OnlyOnce).WithDisplayName("Only once").WithTooltip("A bar mission (single player): once completed, " +
                "no bar offers it again in this game (a failed or discarded one is offered again).").Build();
            context.AddOption<float>(RepeatMinutes).WithDisplayName("Comes again after (minutes)").WithTooltip("A story mission that " +
                "comes again (an encounter): this many minutes of playing time after it was done, as soon as Starts when holds then " +
                "(add a chance to it, random(1, 300) == 1, for a random moment). 0: once.").Delayed().Build();
            context.AddOption<string>(MissionTitle).WithDisplayName("Mission title").WithTooltip("Set it to offer this event as a bar " +
                "mission: a visitor in the multiplayer Space Lounge offers it, and it runs for the squad that takes it (its selectors, " +
                "counts, triggers and scoreboard cover only them). Empty: only /event starts it, for everyone.").Delayed().Build();
            context.AddOption<string>(MissionOffer).WithDisplayName("Offer text").WithTooltip("What the visitor says about the job.")
                .AsTextArea(2, 8).Delayed().Build();
            context.AddOption<string>(MissionClient).WithDisplayName("Client").WithTooltip("Who offers it: a story character (Keith, " +
                "\"Keith as Bob\"), a mod's character (\"vega\", \"frontier_systems:vega\"), or a race and a name (\"vossk K'ekki\", \"terran female Jane\"). Empty: someone of the station's race.")
                .Delayed().Build();
            context.AddOption<float>(MissionReward).WithDisplayName("Reward").WithTooltip("Credits for Mission Complete, split evenly " +
                "across the team.").Delayed().Build();
            context.AddOption<string>(MissionStations).WithDisplayName("Offered at").WithTooltip("Stations whose bars offer it " +
                "(numbers or names, commas between). Empty: every station.").Delayed().Build();
            context.AddOption<float>(MissionChance).WithDisplayName("Offer chance %").WithDefaultValue(35f).WithTooltip("How many of those " +
                "stations' bars offer it, in percent. Each station decides for 30 minutes (docking again doesn't roll it anew), and once " +
                "a team takes it there it isn't offered there again for 30 minutes. 100: every bar, always.").Delayed().Build();
            context.AddOption<EventMissionTakers>(MissionTakers).WithDisplayName("Who can take it").WithTooltip("One In Universe: while a " +
                "squad is on it, no bar anywhere offers it to another squad. One Per Station: a squad taking it at a station takes it out of " +
                "that bar for 30 minutes; other stations still offer it. Anyone: any number of squads, at the same station too.").Build();
            context.AddOption<float>(MinPlayers).WithDisplayName("Min pilots").WithDefaultValue(1f).WithTooltip("The squad needs at least this many pilots.").Delayed().Build();
            context.AddOption<float>(MaxPlayers).WithDisplayName("Max pilots").WithDefaultValue(4f).WithTooltip("And at most this many.").Delayed().Build();
        }
    }

    [Serializable]
    [Node("Flow", null, "Checkpoint")]
    public class EventCheckpointNode : EventFlowNode
    {
        public const string Name = "Name";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<string>(Name).WithTooltip("Optional: the checkpoint's name (one word; empty: the node's id). A save keeps the mission " +
                "as it is here (its variables, objective, target, mission orbits); loading it goes on from here: the On handlers and the Run In " +
                "Parallel / Every blocks before it start again, so do their music, waypoint, respawn and travel commands. Only on the main " +
                "flow from Start (not inside a block). Renaming it later sends old saves back to the start.").Delayed().Build();
    }

    [Serializable]
    [Node("Event", null, "Set Objective")]
    public class EventSetObjectiveNode : EventFlowNode
    {
        public const string Text = "Text", OrbitIn = "Orbit";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            TextIn(context, Text, "", "The objective in the Missions window (\\n = a new line; empty: none).");
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("Optional: the target station (wire Get Orbit in): the gold story icon on the map and the HUD, " +
                "and the window's Show on map. Not wired: no target.").Build();
        }
    }

    [Serializable]
    [Node("Event", null, "Mission Orbit")]
    public class EventQuestOrbitNode : EventFlowNode
    {
        public const string OrbitIn = "Orbit", On = "On";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(On).WithDefaultValue(true).WithTooltip("On: the orbit is the mission's from the next arrival, no normal traffic " +
                "is built there (like a story orbit), until off or the mission's end. Off: normal again.").Build();

        protected override void DefineInputs(IPortDefinitionContext context) =>
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("The orbit (wire Get Orbit in).").Build();
    }

    [Serializable]
    [Node("Event", null, "Start Mission")]
    public class EventStartQuestNode : EventFlowNode
    {
        public const string Quest = "Quest";

        protected override void DefineInputs(IPortDefinitionContext context) =>
            TextIn(context, Quest, "", "The story mission to start beside this one (its graph's file name), unless it is under way or done. Single player.");
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
            TextIn(context, "Speaker", "", "Optional: who asks, with a portrait (a story character like Keith, a mod's character like \"frontier_systems:vega\", a race and a name like \"vossk K'ekki\", or player).");
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
            TextIn(context, "Speaker", "", "Optional: who asks, with a portrait (a story character like Keith, a mod's character like \"frontier_systems:vega\", a race and a name like \"vossk K'ekki\", or player).");
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
                "killer, Victim = the other's name), came back after being destroyed, every enemy the event spawned is gone, or hacked a wreck " +
                "(Spawn's \"wreck\", a mod's derelict; Victim = the wreck's name).").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            context.AddInputPort<Orbit>(OrbitIn).WithTooltip("Optional: only when it happens in this orbit (or at its station). Player Arrives needs it.").Build();
            TextIn(context, "At", "", "Player Arrives: the spot's game coordinates \"x y z\" in that orbit (/pos shows yours).");
            NumberIn(context, "Radius", 1000f, "Player Arrives: how close (metres) a player alive in space must come; again after leaving it.");
            context.AddOutputPort<Players>(PlayerOut).WithTooltip("The player it happened to (the killer for Event Ship Destroyed): wire into a command's Players.").Build();
            context.AddOutputPort<string>(NameOut).WithTooltip("That player's name: wire into a text (\"X was destroyed\"); or type %trigger% in a text.").Build();
            context.AddOutputPort<string>(VictimOut).WithTooltip("Player Killed Player: the destroyed player's name; Wreck Hacked: the wreck's name; or type %victim% in a text.").Build();
        }
    }
}
