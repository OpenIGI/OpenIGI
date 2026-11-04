using System.Globalization;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>What a patrol does at a waypoint once it gets there.</summary>
public enum PatrolPause
{
    /// <summary>Waits a number of seconds.</summary>
    Wait,

    /// <summary>Turns to face a graph node.</summary>
    LookAt,

    /// <summary>Crouches, and stays crouched until the next waypoint.</summary>
    Crouch,

    /// <summary>Plays one of the game's predefined animations, and waits for it to finish.</summary>
    Animation,

    /// <summary>The loop mark: what comes before it runs once (its value is the operand the level wrote, 0 or -1).</summary>
    LoopMark,

    /// <summary>Stops the patrol.</summary>
    Stop,
}

/// <summary>One thing done at a waypoint: what, and its operand (seconds, a node id, an animation number).</summary>
/// <param name="Kind">What is done.</param>
/// <param name="Value">Its operand: seconds for a wait, the node id for a look, the animation's number.</param>
public sealed record PatrolAction(PatrolPause Kind, double Value = 0);

/// <summary>One stop of a patrol: the graph node walked (or run) to, and what is done there before going on.</summary>
/// <param name="Node">The graph node id.</param>
/// <param name="Run">Whether the soldier runs there rather than walks.</param>
/// <param name="Actions">What is done there, in order.</param>
public sealed record PatrolWaypoint(int Node, bool Run, IReadOnlyList<PatrolAction> Actions)
{
    /// <summary>A waypoint walked to with nothing done there.</summary>
    /// <param name="node">The graph node id.</param>
    /// <returns>The waypoint.</returns>
    public static PatrolWaypoint WalkTo(int node) => new(node, false, []);
}

/// <summary>
/// A soldier's patrol as a list of waypoints, read from and written to the original's own <c>PatrolPath</c> task: the
/// <c>PatrolPathCommand</c> children under it, each a command and its operand.
/// </summary>
/// <remarks>
/// <para>
/// The command codes are the game's (<c>PATROLPATHCOMMAND_*</c>, OpenIGI.Game's AiPatrolCommandKind): 0 plays an
/// animation, 1 waits a tick count, 2 walks and 3 runs to a node, 4 crouches, 5 looks at a node, 6 is the loop mark (what
/// comes before it runs once), 7 stops. A wait is in ticks, <see cref="TicksPerSecond"/> a second.
/// </para>
/// <para>
/// The route keeps the layout the level wrote, so a shipped patrol reads and writes back as the same commands: what is done
/// before the first waypoint (<see cref="Start"/>), and the loop mark and the stop as actions where the level put them. A new route is a plain list, as most of level 1's are. A soldier runs its patrol from its own AI
/// script, <c>AIAction_Patrol(&lt;PatrolPath id&gt;, 0, AIACTIONFLAG_NONE)</c> on its idle event (<see cref="SoldierScripts"/>).
/// </para>
/// </remarks>
/// <param name="Waypoints">The stops, in order.</param>
public sealed record PatrolRoute(IReadOnlyList<PatrolWaypoint> Waypoints)
{
    /// <summary>The game's ticks a second (<c>GAME_FREQUENCY</c>).</summary>
    public const int TicksPerSecond = 30;

    /// <summary>The task type a patrol lives in.</summary>
    public const string PathType = "PatrolPath";

    /// <summary>The task type of one command of a patrol.</summary>
    public const string CommandType = "PatrolPathCommand";

    private const int Animation = 0, Delay = 1, WalkTo = 2, RunTo = 3, Crouch = 4, LookAt = 5, LoopMark = 6, Stop = 7;

    /// <summary>Gets an empty route.</summary>
    public static PatrolRoute Empty { get; } = new([]);

    /// <summary>Gets what is done before the first waypoint.</summary>
    public IReadOnlyList<PatrolAction> Start { get; init; } = [];

    /// <summary>Gets whether the route ends with a stop.</summary>
    public bool Stops => (Waypoints.Count > 0 ? Waypoints[^1].Actions : Start) is { Count: > 0 } last && last[^1].Kind == PatrolPause.Stop;

    /// <summary>The route ending with a stop, or not.</summary>
    /// <param name="stop">Whether it should.</param>
    /// <returns>The route.</returns>
    public PatrolRoute WithStop(bool stop)
    {
        if (stop == Stops || Waypoints.Count == 0)
        {
            return this;
        }

        PatrolWaypoint last = Waypoints[^1];
        PatrolAction[] actions = stop ? [.. last.Actions, new PatrolAction(PatrolPause.Stop)] : [.. last.Actions.Take(last.Actions.Count - 1)];
        return this with { Waypoints = [.. Waypoints.Take(Waypoints.Count - 1), last with { Actions = actions }] };
    }

    /// <summary>Reads a patrol from its <c>PatrolPath</c> task.</summary>
    /// <param name="path">The task.</param>
    /// <returns>The route.</returns>
    public static PatrolRoute Read(MissionTask path)
    {
        ArgumentNullException.ThrowIfNull(path);
        List<(int Node, bool Run, List<PatrolAction> Actions)> stops = [];
        List<PatrolAction> start = [];
        foreach (MissionTask command in path.Children.Where(static c => c.TypeName == CommandType))
        {
            (int code, int operand) = Numbers(command);
            if (code is WalkTo or RunTo)
            {
                stops.Add((operand, code == RunTo, []));
                continue;
            }

            (stops.Count > 0 ? stops[^1].Actions : start).Add(code switch
            {
                Delay => new PatrolAction(PatrolPause.Wait, (double)operand / TicksPerSecond),
                LookAt => new PatrolAction(PatrolPause.LookAt, operand),
                Crouch => new PatrolAction(PatrolPause.Crouch, operand),
                LoopMark => new PatrolAction(PatrolPause.LoopMark, operand),
                Stop => new PatrolAction(PatrolPause.Stop, operand),
                _ => new PatrolAction(PatrolPause.Animation, operand),
            });
        }

        return new PatrolRoute([.. stops.Select(static s => new PatrolWaypoint(s.Node, s.Run, s.Actions))]) { Start = start };
    }

    /// <summary>The route's commands, as <c>PatrolPathCommand</c> tasks of a document, labelled as the original's tools label them.</summary>
    /// <param name="document">The mission, which must declare <c>PatrolPathCommand</c>.</param>
    /// <returns>The tasks, unattached and anonymous, in order.</returns>
    public IReadOnlyList<MissionTask> ToCommands(MissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<(int Code, int Operand, string Label)> commands = [.. Start.Select(Command)];
        foreach (PatrolWaypoint stop in Waypoints)
        {
            commands.Add(stop.Run ? (RunTo, stop.Node, Text($"Runs to node id {stop.Node}")) : (WalkTo, stop.Node, Text($"Walks to node id {stop.Node}")));
            commands.AddRange(stop.Actions.Select(Command));
        }

        List<MissionTask> tasks = [];
        foreach ((int code, int operand, string label) in commands)
        {
            MissionTask task = new(CommandType, label);
            document.ApplyDeclaration(task);
            task.FindParameter("Command")?.SetNumbers([code]);
            task.FindParameter("Command Parameter")?.SetNumbers([operand]);
            tasks.Add(task);
        }

        return tasks;
    }

    /// <summary>The step that writes a route into a soldier's patrol, replacing what its <c>PatrolPath</c> held.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="path">The soldier's <c>PatrolPath</c> task.</param>
    /// <returns>One undoable step.</returns>
    public IEditorCommand WriteInto(MissionDocument document, MissionTask path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);
        List<IEditorCommand> steps = [.. path.Children.Where(static c => c.TypeName == CommandType).Select(c => (IEditorCommand)new RemoveTaskCommand(document, c))];
        int at = 0;
        foreach (MissionTask command in ToCommands(document))
        {
            steps.Add(new InsertTaskCommand(document, command, new TaskLocation(path, at++)));
        }

        return new CompositeCommand("Edit patrol", steps);
    }

    private static (int Code, int Operand, string Label) Command(PatrolAction action) => action.Kind switch
    {
        PatrolPause.Wait => (Delay, (int)Math.Round(action.Value * TicksPerSecond), Text($"Delays the script execution for {(int)Math.Round(action.Value * TicksPerSecond)} ticks")),
        PatrolPause.LookAt => (LookAt, (int)action.Value, Text($"Looks at node id {(int)action.Value}")),
        PatrolPause.Crouch => (Crouch, (int)action.Value, "Crouches"),
        PatrolPause.LoopMark => (LoopMark, (int)action.Value, "End script, only runs commands after this one. Takes no paramet"),
        PatrolPause.Stop => (Stop, (int)action.Value, "Quit script, stops script. Takes no parameters"),
        _ => (Animation, (int)action.Value, Text($"Plays predefined animation {(int)action.Value}")),
    };

    private static (int Code, int Operand) Numbers(MissionTask command) => (
        command.FindParameter("Command") is { } c && c.TryGetNumbers(out double[] code) ? (int)code[0] : -1,
        command.FindParameter("Command Parameter") is { } p && p.TryGetNumbers(out double[] operand) ? (int)operand[0] : 0);

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The AI scripts a soldier runs, as the shipped levels write them for every soldier (<c>MISSION:AI/&lt;HumanAI id&gt;</c>):
/// the original loads no other, so a soldier without one does nothing at all.
/// </summary>
public static class SoldierScripts
{
    /// <summary>A guard's: everything to the game's default handling, standing where it was put when idle (level 1's 2226).</summary>
    public const string Guard = """
        if (AIFunction_GetCurrentEventType() == AIEVENT_CREATE)
        {
        	AIFunction_DefaultHandler();
        }
        else if (AIFunction_GetCurrentEventType() == AIEVENT_IDLE)
        {
        }
        else if (AIFunction_GetCurrentEventType() == AIEVENT_ALARMON)
        {
        }
        else if (AIFunction_GetCurrentEventType() == AIEVENT_COMBAT)
        {
        	AIFunction_DefaultHandler();
        }
        else
        {
        	AIFunction_DefaultHandler();
        }
        """;

    /// <summary>A patrolling soldier's: a guard's, walking its patrol when idle (level 1's 2203).</summary>
    /// <param name="patrolPathId">The id of the soldier's <c>PatrolPath</c> task.</param>
    /// <returns>The script's text.</returns>
    public static string Patroller(int patrolPathId) => Guard.ReplaceLineEndings("\n").Replace(
        "AIEVENT_IDLE)\n{\n}",
        string.Create(CultureInfo.InvariantCulture, $"AIEVENT_IDLE)\n{{\n\tAIAction_Patrol({patrolPathId}, 0, AIACTIONFLAG_NONE);\n}}"),
        StringComparison.Ordinal);

    /// <summary>Compiles a script's text into the image the game loads.</summary>
    /// <param name="text">The script.</param>
    /// <returns>The <c>.qvm</c> bytes.</returns>
    public static byte[] Compile(string text) => QscCompiler.Compile(QscParser.ParseScript(text, Scripting.Qvm.QvmProgram.IgiVersion));
}
