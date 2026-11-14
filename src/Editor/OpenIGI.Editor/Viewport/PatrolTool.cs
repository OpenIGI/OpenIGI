using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The patrol tool: a soldier's patrol laid out on the ground as a path editor lays a spline. Clicking the ground adds a
/// waypoint (on the AI graph's nearest node, or a new node there linked into the graph), a waypoint drags, and its menu sets
/// what the soldier does there: walk or run to it, wait, look at a node, crouch. The path draws as the soldier will walk
/// it, along the graph's routes.
/// </summary>
/// <remarks>
/// Every change is one undoable step on the mission: the soldier's <c>PatrolPath</c> and its commands, written as the
/// original's tools write them (<see cref="PatrolRoute"/>), and the graph's nodes. A soldier without an AI graph gets one
/// built round it; its AI script, which the original needs to start the patrol, is written when the mission is saved.
/// </remarks>
/// <param name="workspace">The open mission.</param>
/// <param name="host">The editor host, for the ground.</param>
/// <param name="buildGraph">Builds an AI graph round a world point and returns its task, or null.</param>
/// <param name="notify">Shows a message: the text, and whether it is an error.</param>
internal sealed class PatrolTool(MissionWorkspace workspace, IEditorHost host, Func<Vector3d, MissionTask?> buildGraph, Action<string, bool> notify)
{
    // A click this close to a graph node, in metres, makes it the waypoint rather than a new node.
    private const double SnapMetres = 3;

    // How close, in pixels, a click has to be to a waypoint's marker to take hold of it.
    private const float MarkerReach = 12f;

    private int? _dragged;
    private int? _lookFrom;
    private readonly PatrolPaths _paths = new(workspace);

    /// <summary>Gets the soldier whose patrol is edited, or null.</summary>
    public MissionTask? Soldier { get; private set; }

    /// <summary>Gets the waypoint whose menu is open or which was last touched, by index.</summary>
    public int? Selected { get; private set; }

    /// <summary>Whether a task is a soldier with AI, whose patrol this tool edits.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsSoldier(MissionTask? task) => PatrolPaths.IsSoldier(task);

    /// <summary>Starts editing a soldier's patrol, giving it an AI graph first when it has none.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>Whether the patrol can be edited.</returns>
    public bool Begin(MissionTask soldier)
    {
        if (!IsSoldier(soldier) || workspace.Document is not { } document)
        {
            notify("Select a soldier (a HumanSoldier with HumanAI) to edit its patrol.", false);
            return false;
        }

        Soldier = soldier;
        Selected = null;
        if (GraphTask() is null && TaskPresentation.PositionMetres(soldier) is { } at && buildGraph(at) is { } built
            && Ai() is { } ai && ai.FindParameter("Graph ID") is not null)
        {
            workspace.Execute(SetParameterCommand.Numbers(document, ai, "Graph ID", [built.Id]));
            notify($"Built AI graph {built.Id} round the soldier for it to walk.", false);
        }

        return GraphTask() is not null;
    }

    /// <summary>
    /// Lays the soldier's patrol as a ring round a point: waypoints on the ground, each on the graph's node there or a new
    /// one as a click there would make, walked in turn and round again; one undo step.
    /// </summary>
    /// <param name="centre">What the ring goes round, in world metres.</param>
    /// <param name="radius">How far out, in metres.</param>
    /// <param name="stops">How many waypoints.</param>
    /// <returns>Whether the patrol was laid.</returns>
    public bool Circle(Vector3d centre, double radius, int stops = 8)
    {
        if (Soldier is null || GraphTask() is not { } graphTask)
        {
            return false;
        }

        using (workspace.History.BeginTransaction("Patrol Round"))
        {
            List<PatrolWaypoint> waypoints = [];
            for (int i = 0; i < stops; i++)
            {
                double angle = i * Math.Tau / stops;
                double x = centre.X + (Math.Cos(angle) * radius), y = centre.Y + (Math.Sin(angle) * radius);
                Vector3d at = new(x, y, Height(x, y) ?? centre.Z);
                int? node = (Graph() is { } graph ? NodeNear(graphTask, graph, at) : null) ?? AddNode(graphTask, at);
                if (node is { } id)
                {
                    waypoints.Add(PatrolWaypoint.WalkTo(id));
                }
            }

            Edit(Route with { Waypoints = waypoints }, "Patrol Round");
        }

        return true;
    }

    /// <summary>Stops editing.</summary>
    public void End()
    {
        Soldier = null;
        Selected = null;
        _dragged = null;
        _lookFrom = null;
    }

    /// <summary>Gets the soldier's route as it stands.</summary>
    public PatrolRoute Route => Path() is { } path ? PatrolRoute.Read(path) : PatrolRoute.Empty;

    /// <summary>A press in the view: a waypoint taken hold of, a look-at target picked, or a new waypoint on the ground.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="point">The screen point.</param>
    /// <param name="theme">The theme, for sizes.</param>
    /// <returns>Whether the press was used.</returns>
    public bool Press(ViewportProjector projector, UiVec2 point, EditorTheme theme)
    {
        if (Soldier is null || GraphTask() is not { } graphTask || Graph() is not { } graph)
        {
            return false;
        }

        if (WaypointAt(projector, point, theme.Px(MarkerReach)) is { } index)
        {
            Selected = index;
            _dragged = index;
            return true;
        }

        if (Ground(projector, point) is not { } ground)
        {
            return false;
        }

        // Picking what a waypoint looks at: the node under the click, or a new one there.
        int? node = NodeNear(graphTask, graph, ground) ?? AddNode(graphTask, ground);
        if (node is not { } id)
        {
            return true;
        }

        if (_lookFrom is { } from)
        {
            _lookFrom = null;
            Edit(Route.Waypoints[from] is { } stop ? With(Route, from, stop with { Actions = [.. stop.Actions, new PatrolAction(PatrolPause.LookAt, id)] }) : Route, "Look at");
            return true;
        }

        PatrolRoute route = Route;
        Edit(route with { Waypoints = [.. route.Waypoints, PatrolWaypoint.WalkTo(id)] }, "Add waypoint");
        Selected = route.Waypoints.Count;
        return true;
    }

    /// <summary>A drag of the waypoint taken hold of: its node follows the ground under the pointer.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="point">The screen point.</param>
    /// <returns>Whether a waypoint was being dragged.</returns>
    public bool Drag(ViewportProjector projector, UiVec2 point)
    {
        if (_dragged is not { } index || GraphTask() is not { } graphTask || index >= Route.Waypoints.Count || Ground(projector, point) is not { } ground)
        {
            return _dragged is not null;
        }

        if (workspace.Graphs.MoveNode(graphTask, Route.Waypoints[index].Node, MissionGraphs.ToLocalUnits(graphTask, ground)) is { } move)
        {
            workspace.Execute(move);
        }

        return true;
    }

    /// <summary>Lets go of a dragged waypoint.</summary>
    public void Release()
    {
        if (_dragged is not null)
        {
            _dragged = null;
            workspace.History.BreakMerge();
        }
    }

    /// <summary>A right click: the menu of the waypoint under it.</summary>
    /// <param name="root">The editor root, for the menu.</param>
    /// <param name="projector">The view.</param>
    /// <param name="point">The screen point.</param>
    /// <param name="theme">The theme, for sizes.</param>
    /// <returns>Whether a waypoint was there.</returns>
    public bool OpenMenu(EditorRoot root, ViewportProjector projector, UiVec2 point, EditorTheme theme)
    {
        if (Soldier is null || WaypointAt(projector, point, theme.Px(MarkerReach)) is not { } index)
        {
            return false;
        }

        Selected = index;
        PatrolRoute route = Route;
        PatrolWaypoint stop = route.Waypoints[index];
        List<PatrolEntry> entries =
        [
            new(stop.Run ? "Walk Here Instead of Running" : "Run Here Instead of Walking", () => Edit(With(route, index, stop with { Run = !stop.Run }), "Change gait")),
            new("Wait Here…", () => AskSeconds(root, route, index)),
            new("Look At… (click a spot next)", () => _lookFrom = index),
            new("Crouch Here", () => Edit(With(route, index, stop with { Actions = [.. stop.Actions, new PatrolAction(PatrolPause.Crouch)] }), "Crouch")),
            new("Clear What Is Done Here", () => Edit(With(route, index, stop with { Actions = [.. stop.Actions.Where(static a => a.Kind is PatrolPause.LoopMark or PatrolPause.Stop)] }), "Clear waypoint")),
            new("Remove Waypoint", () => RemoveWaypoint(index)),
            new(route.Stops ? "At the End: Start Again" : "At the End: Stop", () => Edit(route.WithStop(!route.Stops), "Change patrol end")),
        ];
        CommandPalette.Open(root, [.. entries.Select(static e => new PaletteEntry(e.Title, e.Run))], $"Waypoint {index + 1}");
        return true;
    }

    /// <summary>Takes a waypoint out of the route (its graph node stays for others to use).</summary>
    /// <param name="index">The waypoint; the last when null.</param>
    public void RemoveWaypoint(int? index = null)
    {
        PatrolRoute route = Route;
        int at = index ?? route.Waypoints.Count - 1;
        if (at < 0 || at >= route.Waypoints.Count)
        {
            return;
        }

        // What the waypoint carried that runs the patrol (its loop mark, its stop) moves to the one before it.
        PatrolAction[] carried = [.. route.Waypoints[at].Actions.Where(static a => a.Kind is PatrolPause.LoopMark or PatrolPause.Stop)];
        List<PatrolWaypoint> stops = [.. route.Waypoints];
        stops.RemoveAt(at);
        if (carried.Length > 0 && at > 0)
        {
            stops[at - 1] = stops[at - 1] with { Actions = [.. stops[at - 1].Actions, .. carried] };
        }

        Edit(route with { Waypoints = stops }, "Remove waypoint");
        Selected = stops.Count == 0 ? null : Math.Min(at, stops.Count - 1);
    }

    /// <summary>Draws the route: the way the soldier walks it, numbered waypoints, waits, looks and the way back to the start.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="projector">The view.</param>
public void Draw(UiPainter painter, EditorTheme theme, ViewportProjector projector)
    {
        if (Soldier is not { } soldier || _paths.Walk(soldier) is not { } walk)
        {
            return;
        }

        Bgra32 walking = theme.Accent, run = Bgra32.FromRgb(0xE8, 0x8A, 0x2E);
        static Vector3d Up(Vector3d p) => p + new Vector3d(0, 0, 0.15);

        // The way the soldier walks it, and round again to the first waypoint faintly unless the route stops at its end.
        foreach (PatrolLeg leg in walk.Legs)
        {
            Line(painter, projector, Up(leg.From), Up(leg.To), leg.IsWayBack ? walking.WithAlpha(90) : leg.Run ? run : walking, theme.Px(leg.IsWayBack ? 1.6f : leg.Run ? 3f : 2.4f));
        }

        foreach ((Vector3d from, Vector3d to) in walk.Looks)
        {
            Line(painter, projector, Up(from), Up(to), Bgra32.FromRgb(0xC8, 0xC8, 0x60).WithAlpha(200), theme.Px(1.4f));
        }

        foreach (PatrolStop stop in walk.Stops)
        {
            if (!projector.TryProject(Up(stop.At), out UiVec2 screen))
            {
                continue;
            }

            bool selected = stop.Index == Selected;
            float radius = theme.Px(selected ? 10f : 8.5f);
            painter.FillDot(screen, radius + theme.Px(1.5f), theme.Background);
            painter.FillDot(screen, radius, stop.Stop.Run ? run : walking);
            string number = (stop.Index + 1).ToString(CultureInfo.InvariantCulture);
            float width = theme.SmallFont.MeasureWidth(number, theme.SmallTextSize);
            painter.DrawText(theme.SmallFont, number, new UiVec2(screen.X - (width / 2), screen.Y - (theme.SmallTextSize * 0.62f)), theme.SmallTextSize, Bgra32.White);
            string notes = string.Join("  ", stop.Stop.Actions.Select(Note).Where(static n => n.Length > 0));
            if (notes.Length > 0)
            {
                painter.DrawText(theme.SmallFont, notes, new UiVec2(screen.X + radius + theme.Px(5f), screen.Y - theme.Px(7f)), theme.SmallTextSize, theme.Text);
            }
        }
    }

    /// <summary>The line the view shows while the tool is on: what to do next.</summary>
    public string Hint => _lookFrom is not null
        ? "Click the spot the soldier should look at."
        : Route.Waypoints.Count == 0
            ? "Click the ground to lay the soldier's patrol, waypoint by waypoint."
            : "Click to add a waypoint  •  drag one to move it  •  right-click one for what to do there  •  Backspace removes the last  •  Esc done";

    private static string Note(PatrolAction action) => action.Kind switch
    {
        PatrolPause.Wait => string.Create(CultureInfo.InvariantCulture, $"wait {action.Value:0.#}s"),
        PatrolPause.LookAt => "look",
        PatrolPause.Crouch => "crouch",
        PatrolPause.Animation => string.Create(CultureInfo.InvariantCulture, $"anim {action.Value:0}"),
        PatrolPause.Stop => "stop",
        _ => string.Empty,
    };

    private static void Line(UiPainter painter, ViewportProjector projector, Vector3d a, Vector3d b, Bgra32 colour, float width)
    {
        if (projector.TryProject(a, out UiVec2 from) && projector.TryProject(b, out UiVec2 to))
        {
            painter.StrokeSoft(from, to, width, colour);
        }
    }

    private static PatrolRoute With(PatrolRoute route, int index, PatrolWaypoint stop) =>
        route with { Waypoints = [.. route.Waypoints.Select((w, i) => i == index ? stop : w)] };

    private void AskSeconds(EditorRoot root, PatrolRoute route, int index)
    {
        CommandPalette palette = CommandPalette.Open(root, [], "Seconds to wait here");
        palette.Typed = text => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0
            ? new PaletteEntry(string.Create(CultureInfo.InvariantCulture, $"Wait {seconds:0.#} seconds"), () =>
            {
                PatrolWaypoint stop = route.Waypoints[index];
                Edit(With(route, index, stop with { Actions = [.. stop.Actions, new PatrolAction(PatrolPause.Wait, seconds)] }), "Wait");
            })
            : null;
    }

    // Writes a route into the soldier's patrol, making the soldier's PatrolPath first when it has none, as one step.
    private void Edit(PatrolRoute route, string name)
    {
        if (Soldier is not { } soldier || workspace.Document is not { } document)
        {
            return;
        }

        List<IEditorCommand> steps = [];
        MissionTask? path = Path();
        if (path is null)
        {
            HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
            int id = Enumerable.Range(1, MissionDocument.MaximumTaskId - 1).FirstOrDefault(i => !used.Contains(i), -1);
            path = new MissionTask(PatrolRoute.PathType, "Patrol", id);
            document.ApplyDeclaration(path);
            steps.Add(new InsertTaskCommand(document, path, new TaskLocation(soldier, soldier.Children.Count)));
        }

        steps.Add(route.WriteInto(document, path));
        workspace.Execute(new CompositeCommand(name, steps));
    }

    private MissionTask? Ai() => Soldier is { } soldier ? PatrolPaths.Ai(soldier) : null;

    private MissionTask? Path() => Soldier is { } soldier ? PatrolPaths.Path(soldier) : null;

// The AIGraph task the soldier's HumanAI names.
    private MissionTask? GraphTask() => Soldier is { } soldier ? _paths.GraphTask(soldier) : null;

    private GraphDocument? Graph() => Soldier is { } soldier ? _paths.Graph(soldier) : null;

    private static int? NodeNear(MissionTask graphTask, GraphDocument graph, Vector3d at)
    {
        (int Id, double Distance) best = (-1, double.MaxValue);
        foreach (GraphDocumentNode node in graph.Nodes.Where(static n => n.PositionRecord >= 0))
        {
            double distance = Vector3d.Distance(MissionGraphs.ToWorldMetres(graphTask, node.Position), at);
            if (distance < best.Distance)
            {
                best = (node.Id, distance);
            }
        }

        return best.Distance <= SnapMetres ? best.Id : null;
    }

    private int? AddNode(MissionTask graphTask, Vector3d at)
    {
        if (workspace.Graphs.AddNode(graphTask, at, Height, out int id) is not { } add)
        {
            return null;
        }

        workspace.Execute(add);
        _paths.Forget();
        return id;
    }

    private double? Height(double x, double y) =>
        host.Terrain is { } terrain && terrain.TryGetHeight(x, y, out double z) ? z : null;

    private Vector3d? Ground(ViewportProjector projector, UiVec2 point) =>
        projector.TryRay(point, out Vector3d ray) && host.Terrain is { } terrain && terrain.TryRaycast(projector.Eye, ray, 2000, out Vector3d hit) ? hit : null;

    private int? WaypointAt(ViewportProjector projector, UiVec2 point, float reach)
    {
        if (GraphTask() is not { } graphTask || Graph() is not { } graph)
        {
            return null;
        }

        PatrolRoute route = Route;
        for (int i = route.Waypoints.Count - 1; i >= 0; i--)
        {
            if (graph.Nodes.FirstOrDefault(n => n.Id == route.Waypoints[i].Node) is { PositionRecord: >= 0 } node
                && projector.TryProject(MissionGraphs.ToWorldMetres(graphTask, node.Position) + new Vector3d(0, 0, 0.15), out UiVec2 screen)
                && MathF.Sqrt(((screen.X - point.X) * (screen.X - point.X)) + ((screen.Y - point.Y) * (screen.Y - point.Y))) <= reach)
            {
                return i;
            }
        }

        return null;
    }

    private sealed record PatrolEntry(string Title, Action Run);
}
