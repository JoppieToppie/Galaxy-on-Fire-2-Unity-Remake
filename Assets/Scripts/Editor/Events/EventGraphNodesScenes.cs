// EventGraphNodesScenes.cs
// The EventGraph's nodes: ship orders (EventShipOrders) and cutscenes (EventCutscene). The base classes, flow and triggers are in EventGraphNodes.cs.

using System;
using System.Collections.Generic;
using GoF2Remake.Multiplayer;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.EditorTools
{
    // ---- ship orders (EventShipOrders) ---------------------------------------------------------------------------------

    /// <summary>A ship order: the ship by the name its Spawn node gave it (a numbered group "Name 1".. all take it).</summary>
    [Serializable]
    public abstract class EventShipOrderNode : EventCommandNode
    {
        public const string Ship = "Ship";
        protected const string Places = "A place: \"x y z\" or \"station x y z\" (game coordinates of the orbit, /pos shows yours), " +
            "\"player right up forward\" (from the player's ship: 0 0 5000 ahead of it), \"ship <name> [x y z]\".";

        protected static void ShipIn(IPortDefinitionContext c) =>
            TextIn(c, Ship, "", "The ship: the name its Spawn node gave it (all of a numbered group, \"Name 1\", \"Name 2\"...).");
    }

    [Serializable]
    [Node("Ships", null, "Fly To")]
    public class EventFlyToNode : EventShipOrderNode
    {
        public const string To = "To", Then = "Then", Speed = "Speed", Radius = "Radius", Fight = "Fight";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<EventShipThen>(Then).WithTooltip("When it is there: Resume its own flying and fighting, Hold still (a target " +
                "that neither flies nor shoots), Vanish, or Jump Out.").Build();
            context.AddOption<bool>(Fight).WithTooltip("It fights the enemies it meets on the way, then goes on.").Build();
        }

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            ShipIn(context);
            TextIn(context, To, "", "Where it flies. " + Places);
            NumberIn(context, Speed, 0f, "Units per ms (0 = its own: a fighter 2, a freighter 1; boosting about 8).");
            NumberIn(context, Radius, 0f, "How close counts as there, in game units (0 = 2500: ships can't turn on the spot).");
        }
    }

    [Serializable]
    [Node("Ships", null, "Fly Route")]
    public class EventFlyRouteNode : EventShipOrderNode
    {
        public const string Route = "Route", Loop = "Loop", Then = "Then", Speed = "Speed", Radius = "Radius", Fight = "Fight";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<bool>(Loop).WithTooltip("Round and round (a patrol); Then never comes.").Build();
            context.AddOption<EventShipThen>(Then).WithTooltip("At the last point: Resume, Hold, Vanish or Jump Out.").Build();
            context.AddOption<bool>(Fight).WithTooltip("It fights the enemies it meets on the way, then goes on.").Build();
        }

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            ShipIn(context);
            TextIn(context, Route, "", "The places it flies through, / between them (\"0 0 20000 / 8000 0 30000 / station 0 0 5000\"). " + Places);
            NumberIn(context, Speed, 0f, "Units per ms (0 = its own).");
            NumberIn(context, Radius, 0f, "How close counts as there (0 = 2500 game units).");
        }
    }

    [Serializable]
    [Node("Ships", null, "Follow")]
    public class EventFollowNode : EventShipOrderNode
    {
        public const string Leader = "Leader", Offset = "Offset", Speed = "Speed", Fight = "Fight";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(Fight).WithDisplayName("Escort").WithTooltip("It fights the enemies it meets, then comes back to its place.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            ShipIn(context);
            TextIn(context, Leader, "player", "Whom it follows: player, or ship <name>.");
            TextIn(context, Offset, "", "Its place beside the leader, \"right up forward\" in game units (empty: 0 0 -1500, behind). A group spreads out from it.");
            NumberIn(context, Speed, 0f, "Its top speed (0 = keeps up by itself).");
        }
    }

    [Serializable]
    [Node("Ships", null, "Attack")]
    public class EventAttackNode : EventShipOrderNode
    {
        public const string TargetIn = "Target";

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            ShipIn(context);
            TextIn(context, TargetIn, "player", "What it attacks: player, or ship <name> (whoever's side it is on). When that is gone it resumes.");
        }
    }

    [Serializable]
    [Node("Ships", null, "Ship Action")]
    public class EventShipActionNode : EventShipOrderNode
    {
        public const string Action = "Action", Seconds = "Seconds";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventShipAction>(Action).WithTooltip("Hold: stays where it is (neither flies nor shoots). Resume: its own flying " +
                "and fighting again. Jump Out: accelerates away and is gone. Dock: flies into the station and is gone. Flee: away from " +
                "the player at full speed, then jumps out.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            ShipIn(context);
            NumberIn(context, Seconds, 6f, "Flee: how long it runs before it jumps out.");
        }
    }

    // ---- cutscenes (EventCutscene) ---------------------------------------------------------------------------------

    [Serializable]
    [Node("Cutscene", null, "Start Cutscene")]
    public class EventStartCutsceneNode : EventCommandNode
    {
        public const string Letterbox = "Letterbox", Freeze = "Freeze", Invulnerable = "Invulnerable";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<bool>(Letterbox).WithDefaultValue(true).WithTooltip("The cinema bars slide in at the top and bottom.").Build();
            context.AddOption<bool>(Freeze).WithDisplayName("Freeze ship").WithTooltip("The player's ship holds still (else it flies on, straight).").Build();
            context.AddOption<bool>(Invulnerable).WithDefaultValue(true).WithTooltip("The player takes no damage during the cutscene.").Build();
        }

        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Cutscene", null, "End Cutscene")]
    public class EventEndCutsceneNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Cutscene", null, "Camera Shot")]
    public class EventCameraShotNode : EventCommandNode
    {
        public const string From = "From", To = "To", LookAt = "LookAt", Seconds = "Seconds", Follow = "Follow", Fov = "Fov", Shake = "Shake";
        const string Places = "A place: \"player right up forward\" (game units in the ship's own frame: 0 600 -1338 is the chase " +
            "spot, 0 0 3000 in front), \"station x y z\" or \"x y z\" (game coordinates of the orbit, /pos shows yours), \"ship <name> " +
            "[x y z]\" (a ship Spawn named).";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(Follow).WithTooltip("The places that move (the player, a ship) are followed every frame; off: read " +
                "once when the shot starts.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            TextIn(context, From, "player 0 600 -2500", "Where the camera is. " + Places);
            TextIn(context, To, "", "Optional: where it moves to over Seconds (eased). " + Places);
            TextIn(context, LookAt, "player", "What it looks at every frame (default the player's ship). " + Places);
            NumberIn(context, Seconds, 4f, "How long the move takes (the shot itself lasts until the next one, Chase Camera or End Cutscene; " +
                "add a Wait to hold it).");
            NumberIn(context, Fov, 0f, "Field of view in degrees (0 = the game's 70).");
            NumberIn(context, Shake, 0f, "Camera shake, 0 to 1.");
        }
    }

    [Serializable]
    [Node("Cutscene", null, "Chase Camera")]
    public class EventChaseCameraNode : EventCommandNode
    {
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Cutscene", null, "Fade")]
    public class EventFadeNode : EventCommandNode
    {
        public const string Direction = "Direction", Seconds = "Seconds", Colour = "Colour";

        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<EventFade>(Direction).WithTooltip("Out: to the colour, then held. In: from the colour back to the " +
                "game. Clear: gone at once.").Build();

        protected override void DefineInputs(IPortDefinitionContext context)
        {
            PlayersIn(context);
            NumberIn(context, Seconds, 1f, "How long the fade takes.");
            TextIn(context, Colour, "000000", "The colour as rrggbb (000000 black, ffffff white). Conversations and the radio stay readable over it.");
        }
    }

    [Serializable]
    [Node("Cutscene", null, "Letterbox")]
    public class EventLetterboxNode : EventCommandNode
    {
        public const string On = "On";
        protected override void OnDefineOptions(IOptionDefinitionContext context) =>
            context.AddOption<bool>(On).WithDefaultValue(true).WithTooltip("The cinema bars on (or off), without a cutscene's other effects.").Build();
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Play Sound")]
    public class EventPlaySoundNode : EventCommandNode
    {
        public const string Sound = "Sound", ModSound = "ModSound";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<EventSound>(Sound).Build();
            context.AddOption<string>(ModSound).WithDisplayName("Mod sound").WithTooltip("Optional: a mod's sound instead (its sounds/<name>.ogg " +
                "file's name, a mod that is on); empty: the Sound above.").Delayed().Build();
        }
        protected override void DefineInputs(IPortDefinitionContext context) => PlayersIn(context);
    }

    [Serializable]
    [Node("Commands", null, "Play Music")]
    public class EventPlayMusicNode : EventCommandNode
    {
        public const string Music = "Music", Track = "Track";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<EventMusic>(Music).WithTooltip("Looped instead of the game's music until Stop Music or the event's end.").Build();
            context.AddOption<string>(Track).WithDisplayName("Mod track").WithTooltip("Optional: a mod's track instead (its music/<name>.ogg file's " +
                "name, a mod that is on); empty: the Music above.").Delayed().Build();
        }

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

    /// <summary>Lua code of the mod the graph belongs to, run when the flow reaches it (EventRunner's "lua" line,
    /// Modding.ModScripts.RunCode): for what no node does. The mod's scripts must be allowed by the player.</summary>
    [Serializable]
    [Node("Commands", null, "Lua")]
    public class EventLuaNode : EventCommandNode
    {
        public const string Code = "Code", Result = "Result";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>(Code).WithTooltip("Lua code, run in this mod's scripts (the game table, the game's classes, your own globals). " +
                "\"local event = ...\" gives event.name, event.get(\"expression\") (a value of this graph: a variable, kills, credits ...) and " +
                "event.set(\"variable\", number). It runs where the event runs (single player, or the host / server).")
                .AsTextArea(4, 20).Delayed().Build();
            context.AddOption<string>(Result).WithTooltip("Optional: a variable for the number or true / false the code returns.").Delayed().Build();
        }
    }
}
