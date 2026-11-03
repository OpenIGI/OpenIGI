using OpenIGI.Editor.Core.Commands;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Configuration;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Editor.Panels;
using OpenIGI.Editor.Terrain;
using OpenIGI.Editor.Viewport;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Docking;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;
using OpenIGI.UI.Text;

namespace OpenIGI.Editor;

/// <summary>The editor's console verbs (<c>editor select|find|move|turn|pick|sculpt|do</c>), queued and run in order.</summary>
internal sealed partial class EditorSession
{
    /// <inheritdoc/>
    public string? RunCommand(IReadOnlyList<string> arguments)
    {
        if (arguments is [string run, string id] && run.Equals("do", StringComparison.OrdinalIgnoreCase))
        {
            // Run once the mission is open and any selection asked for before it has been made, in the order given.
            EditorAction? action = _root.Actions.All.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (action is null)
            {
                return $"No editor action '{id}'.";
            }

            _pendingSteps.Enqueue(() =>
            {
                _host.Logger.Information("Editor", action.TryExecute() ? $"Ran {action.Id}." : $"{action.Id} could not run.");
                return true;
            });
            return $"Running {action.Id} once the mission is open.";
        }

        if (arguments is [string find, ..] && find.Equals("find", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 2)
        {
            string query = string.Join(' ', arguments.Skip(1));
            _dock.Show("content");
            return _content.Find(query) ? $"Found '{query}': {_content.PreviewFacts}" : $"No file name contains '{query}'.";
        }

        // editor config <path>: a config script opened in its form editor, as double-clicking it in Content would.
        if (arguments is [string configVerb, string configPath] && configVerb.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            string path = configPath.Contains(':', StringComparison.Ordinal) ? configPath : "LOCAL:" + configPath;
            _pendingSteps.Enqueue(() =>
            {
                _content.OpenConfig?.Invoke(path);
                _host.Logger.Information("Editor", $"Opened {path} in its config editor.");
                return true;
            });
            return $"Opening {path} in its config editor once the mission is open.";
        }

        // editor drag <file> <fx> <fy> [drop]: a content file held over the view (fractions of its size), as a mouse drag
        // holds it, or dropped there; "editor drag none" lets it go.
        if (arguments is [string dragVerb, string dragged, ..] && dragVerb.Equals("drag", StringComparison.OrdinalIgnoreCase))
        {
            if (dragged.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                _viewport.ScriptDrag(null, default);
                return "Let the drag go.";
            }

            if (arguments.Count < 4 || !float.TryParse(arguments[2], System.Globalization.CultureInfo.InvariantCulture, out float fx)
                || !float.TryParse(arguments[3], System.Globalization.CultureInfo.InvariantCulture, out float fy))
            {
                return "editor drag <file> <fx> <fy> [drop], fractions of the view.";
            }

            bool drop = arguments.Count > 4 && arguments[4].Equals("drop", StringComparison.OrdinalIgnoreCase);

            // In turn with the other steps, once the level has settled, its objects are linked and the camera has stopped
            // gliding to where the level puts it (a second, as a scripted pick waits).
            int settled = 0;
            _pendingSteps.Enqueue(() =>
            {
                settled = _host.IsWarmingUp || _live.LinkedCount == 0 ? 0 : settled + 1;
                if (settled < 60)
                {
                    return false;
                }

                if (!_content.Find(dragged) || _content.Selected is not { } file)
                {
                    _host.Logger.Information("Editor", $"editor drag: no file name contains '{dragged}'.");
                    return true;
                }

                _viewport.ScriptDrag(file, new UI.Layout.UiVec2(fx, fy), drop);
                _host.Logger.Information("Editor", FormattableString.Invariant($"{(drop ? "Dropped" : "Dragging")} {file.Name} at {fx}, {fy} of the view."));
                return true;
            });
            return FormattableString.Invariant($"{(drop ? "Dropping" : "Dragging")} '{dragged}' at {fx}, {fy} of the view once it is up.");
        }

        if (arguments is [string move, string dx, string dy, string dz] && move.Equals("move", StringComparison.OrdinalIgnoreCase))
        {
            // Moves the selection by metres, as a drag of the move gizmo would: for scripted checks.
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(dx, System.Globalization.NumberStyles.Float, invariant, out double x)
                || !double.TryParse(dy, System.Globalization.NumberStyles.Float, invariant, out double y)
                || !double.TryParse(dz, System.Globalization.NumberStyles.Float, invariant, out double z))
            {
                return "editor move <dx> <dy> <dz>, in metres.";
            }

            Vector3d by = new(x, y, z);
            _pendingSteps.Enqueue(() => MoveSelection(by));
            return "Moving the selection once something is picked.";
        }

        if (arguments is [string turn, string amount] && turn.Equals("turn", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(amount, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double degrees))
        {
            // Turns the selection about the world's up axis, as a drag of the rotate gizmo's up ring would: for scripted checks.
            _pendingSteps.Enqueue(() => TurnSelection(degrees));
            return FormattableString.Invariant($"Turning the selection {degrees}° once something is selected.");
        }

        if (arguments is [string pick] && pick.Equals("pick", StringComparison.OrdinalIgnoreCase))
        {
            // Clicks the middle of the viewport, for scripted checks.
            _pendingPick = true;
            return "Picking at the middle of the view once it is up.";
        }

        if (arguments is [string sculpt, ..] && sculpt.Equals("sculpt", StringComparison.OrdinalIgnoreCase))
        {
            // Paints the terrain brush at the middle of the viewport for a while, as a held stroke would: for scripted
            // checks, which cannot move a mouse.
            bool inverted = arguments.Skip(1).Any(static a => a.Equals("invert", StringComparison.OrdinalIgnoreCase));
            double holdFor = arguments is [_, string time, ..] && double.TryParse(time, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds)
                ? Math.Clamp(seconds, 0.0, 30.0)
                : 1.0;

            // In turn with the other steps, once the level has settled where it puts the camera.
            _pendingSteps.Enqueue(() =>
            {
                if (_host.IsWarmingUp || _host.Terrain is null)
                {
                    return false;
                }

                _host.Logger.Information("Editor", _viewport.SculptAtCentre(holdFor, inverted) ? "Sculpted at the middle of the view." : "No sculptable ground under the middle of the view.");
                return true;
            });
            return FormattableString.Invariant($"Sculpting for {holdFor:0.##} s once the view is up.");
        }

        if (arguments is [string brushVerb, ..] && brushVerb.Equals("brush", StringComparison.OrdinalIgnoreCase))
        {
            // Sets the terrain brush for scripted strokes: "brush radius 40", "brush strength 20", "brush layer 2", or several.
            for (int i = 1; i + 1 < arguments.Count; i += 2)
            {
                if (!double.TryParse(arguments[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    return $"'{arguments[i + 1]}' is not a number.";
                }

                switch (arguments[i].ToLowerInvariant())
                {
                    case "radius":
                        _terrainTool.Brush.RadiusMetres = Math.Clamp(value, 0.5, 500.0);
                        break;
                    case "layer":
                        _terrainTool.Brush.PaintLayer = (int)value;
                        break;
                    case "strength":
                        _terrainTool.Brush.Strength = Math.Clamp(value, 0.05, 100.0);
                        break;
                    default:
                        return $"The brush has no '{arguments[i]}'; it has radius, strength and layer.";
                }
            }

            return FormattableString.Invariant($"Brush: {_terrainTool.Brush.RadiusMetres:0.#} m, paint layer {_terrainTool.Brush.PaintLayer}.");
        }

        if (arguments is [string near, string sx, string sy, ..] && near.Equals("near", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(sx, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double nx)
            && double.TryParse(sy, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ny))
        {
            // Selects the placed object nearest a point (x y, in metres), or every one within a radius: for scripted edits of
            // objects with no name.
            double radius = arguments is [_, _, _, string r] && double.TryParse(r, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double within)
                ? within
                : 0.0;
            _pendingSteps.Enqueue(() => SelectNear(nx, ny, radius));
            return FormattableString.Invariant($"Selecting near ({nx}, {ny}) once the mission is open.");
        }

        if (arguments is [string make, string from, _, ..] && make.Equals("newmission", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(from, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int baseId))
        {
            // Makes a mission from another, as the New Mission panel does: for scripted checks.
            string title = string.Join(' ', arguments.Skip(2));
            _pendingSteps.Enqueue(() =>
            {
                if (_host.Missions.FirstOrDefault(m => m.Id == baseId) is not { } source)
                {
                    _host.Logger.Warning("Editor", $"No mission {baseId} to make a mission from.");
                    return true;
                }

                int id = _host.Missions.Max(static m => m.Id) + 1;
                CreateMission(new Panels.NewMissionRequest(source, title, id, JoinCampaign: true, Core.Projects.MissionTarget.Retail));
                return true;
            });
            return $"Making mission '{title}' from mission {baseId} once the editor is open.";
        }

        if (arguments is [string game, string titleId, ..] && game.Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            // Switches to another game and opens the editor again there, running what follows as an editor command:
            // "game igi2 wizard newmission retail".
            string[] then = [.. arguments.Skip(2)];
            _host.SwitchTitle(titleId, then.Length > 0 ? [then] : []);
            return $"Switching to {titleId}.";
        }

        if (arguments is [string wizard, string which, ..] && wizard.Equals("wizard", StringComparison.OrdinalIgnoreCase)
            && which.Equals("newmission", StringComparison.OrdinalIgnoreCase))
        {
            // Opens the new-mission wizard, as the menu does, with a target and a name to start on: what a switch of game
            // leaves to run, so the wizard comes back where it was.
            Core.Projects.MissionTarget target = arguments.Count > 2 && arguments[2].Equals("openigi", StringComparison.OrdinalIgnoreCase)
                ? Core.Projects.MissionTarget.OpenIgi
                : Core.Projects.MissionTarget.Retail;
            string named = string.Join(' ', arguments.Skip(3));
            _pendingSteps.Enqueue(() =>
            {
                if (_host.IsWarmingUp)
                {
                    return false;
                }

                OpenNewMission(target, named);
                return true;
            });
            return "Opening the new-mission wizard.";
        }

        if (arguments is not [string verb, ..] || !verb.Equals("select", StringComparison.OrdinalIgnoreCase) || arguments.Count < 2)
        {
            return null;
        }

        string name = string.Join(' ', arguments.Skip(1));
        _pendingSteps.Enqueue(() => SelectNamed(name));
        return $"Selecting '{name}' once the mission is open.";
    }

    // Steps asked for by command, run in order once the mission is open and the view has been drawn; a step that returns
    // false is tried again next frame.
    private readonly Queue<Func<bool>> _pendingSteps = new();

    // A click at the middle of the view asked for by command, made once the view is up and linked.
    private bool _pendingPick;

    // How many frames the view has been ready for a pending pick.
    private int _pickReadyFrames;

    // Moves the selection by metres, as a drag of the move gizmo would, with whatever it carries; false while nothing
    // movable is selected.
    private bool MoveSelection(Vector3d by)
    {
        if (_workspace.Document is not { } document || _workspace.Selection.Count == 0)
        {
            return false;
        }

        List<MissionTask> moving = [.. _workspace.Selection.Items];
        List<IEditorCommand> moves = [];
        List<string> names = [];
        foreach (MissionTask task in moving.Concat(ObjectCarry.Carried(document, _live, moving)))
        {
            if (TaskPresentation.Position(task) is { } position && TaskPresentation.PositionMetres(task) is { } at && !MissionGraphs.IsGraph(task))
            {
                names.Add(TaskPresentation.Name(task));
                Vector3d to = at + by;
                moves.Add(SetParameterCommand.Numbers(document, task, position.Name, [WorldUnits.FromMetres(to.X), WorldUnits.FromMetres(to.Y), WorldUnits.FromMetres(to.Z)]));
            }
        }

        if (moves.Count == 0)
        {
            return false;
        }

        _workspace.Execute(moves.Count == 1 ? moves[0] : new CompositeCommand("Move", moves));
        _host.Logger.Information("Editor", FormattableString.Invariant($"Moved {moves.Count} object(s) by ({by.X}, {by.Y}, {by.Z}) m: {string.Join(", ", names)}."));
        return true;
    }

    // Turns the selection about the up axis and writes it as the rotate gizmo does (a heading alone for a type that keeps
    // one), swinging what it carries round with it.
    private bool TurnSelection(double degrees)
    {
        if (_workspace.Selection.Primary is not { } turning || _workspace.Document is not { } document
            || TaskPresentation.Orientation(turning) is not { } orientation)
        {
            return false;
        }

        Matrix3f world = Matrix3f.RotationAxis(new Vector3d(0, 0, 1), (float)(degrees * Math.PI / 180.0));
        Matrix3f turned = TaskPresentation.OrientationMatrix(turning) * world.Transposed();
        Vector3f angles = turned.ToEngineEulerAngles();
        SetParameterCommand turn = TaskPresentation.TurnsAboutUpOnly(turning)
            ? SetParameterCommand.Numbers(document, turning, orientation.Name, [angles.Z])
            : SetParameterCommand.Numbers(document, turning, orientation.Name, [angles.X, angles.Y, angles.Z]);
        IReadOnlyList<MissionTask> carried = ObjectCarry.Carried(document, _live, [turning]);
        _workspace.Execute(carried.Count == 0 || TaskPresentation.PositionMetres(turning) is not { } pivot
            ? turn
            : new CompositeCommand(turn.Name, [turn, .. ObjectCarry.Turn(document, ObjectCarry.Snapshot(carried), pivot, world)]));
        _host.Logger.Information("Editor", FormattableString.Invariant($"Turned {TaskPresentation.Name(turning)} {degrees}° with {carried.Count} carried: {orientation.Name} now {angles.X:0.###}, {angles.Y:0.###}, {angles.Z:0.###}."));
        return true;
    }

    // The object a script names, selected and framed; in turn with the other scripted steps.
    private bool SelectNamed(string name)
    {
        if (_workspace.Document is not { } document)
        {
            return false;
        }

        // '#12' is the task with id 12, for tasks with no label such as AI graphs.
        int id = -1;
        bool byId = name.StartsWith('#') && int.TryParse(name.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id);
        MissionTask? task = byId
            ? document.EnumerateTasks().FirstOrDefault(t => t.Id == id)
            : document.EnumerateTasks().FirstOrDefault(t => string.Equals(t.Label, name, StringComparison.OrdinalIgnoreCase))
            ?? document.EnumerateTasks().FirstOrDefault(t => t.Label.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            _host.Logger.Warning("Editor", $"No object is called '{name}'.");
            return true;
        }

        _workspace.Selection.Set(task);
        _viewport.Frame(task);
        _host.Logger.Information("Editor", $"Selected {TaskPresentation.Name(task)}.");
        return true;
    }

    // The placed object nearest a point on the ground plane, or all within a radius of it.
    private bool SelectNear(double x, double y, double radius)
    {
        if (_workspace.Document is not { } document)
        {
            return false;
        }

        List<(MissionTask Task, double Distance)> placed = [];
        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (task.FindParameter(Game.World.WorldScene.ModelParameter) is not null
                && TaskPresentation.Position(task) is { } position
                && position.TryGetNumbers(out double[] at)
                && at.Length >= 2)
            {
                double dx = WorldUnits.ToMetres(at[0]) - x;
                double dy = WorldUnits.ToMetres(at[1]) - y;
                placed.Add((task, Math.Sqrt((dx * dx) + (dy * dy))));
            }
        }

        MissionTask[] chosen = radius > 0.0
            ? [.. placed.Where(p => p.Distance <= radius).Select(static p => p.Task)]
            : [.. placed.OrderBy(static p => p.Distance).Take(1).Select(static p => p.Task)];
        _workspace.Selection.Set(chosen);
        _host.Logger.Information("Editor", $"Selected {chosen.Length} object(s): {string.Join(", ", chosen.Select(TaskPresentation.Name))}.");
        return true;
    }
}
