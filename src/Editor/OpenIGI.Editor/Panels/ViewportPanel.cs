using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Terrain;
using OpenIGI.Editor.Viewport;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>Which gizmo the viewport shows on the selection.</summary>
internal enum ViewportTool
{
    /// <summary>The move gizmo.</summary>
    Move,

    /// <summary>The rotate gizmo.</summary>
    Rotate,

    /// <summary>The resize gizmo, on volumes: trigger, spawn and ambient-sound areas.</summary>
    Scale,

    /// <summary>The terrain brush, shaping the ground.</summary>
    Terrain,

    /// <summary>The patrol tool, laying the selected soldier's patrol on the ground.</summary>
    Patrol,
}

/// <summary>
/// The world view. Hold the right mouse button to look and fly (W A S D, Q E, Shift faster), the middle button to
/// pan, the wheel to dolly; click to select, drag the gizmo to move, F to frame the selection. With no mission
/// open it is the start page that opens one.
/// </summary>
internal sealed partial class ViewportPanel : EditorElement
{
    // How far a click reaches into the world.
    private const double PickReachMetres = 5000.0;

    private static readonly KeyCode[] FlightKeys = [KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.LeftShift, KeyCode.LeftControl];

    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly LiveWorldLink _live;
    private readonly TranslateGizmo _gizmo = new();
    private readonly RotateGizmo _rotate = new();
    private readonly GraphOverlay _graph = new();
    private readonly BoxGizmo _box = new();
    private readonly TerrainTool _terrain;
    private MissionTask? _graphTask;
    private readonly EditorCamera _camera = new();
    private readonly Dictionary<MissionTask, Vector3d> _moveStarts = new(ReferenceEqualityComparer.Instance);
    private double? _gridHeight;
    private IReadOnlyList<(MissionTask Task, Vector3d? Position, Matrix3f? Orientation)> _turnCarried = [];
    private readonly OrientationGizmo _orientation = new();
    private MissionTask? _hovered;
    private int _hoveredMission = -1;

    public ViewportPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace, LiveWorldLink live, TerrainTool terrain)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _live = live;
        _terrain = terrain;
    }

    private bool Sculpting => Tool == ViewportTool.Terrain && _terrain.IsAvailable;

    /// <summary>Raised when Shift-dragging a move handle asks for the selection to be duplicated before it moves.</summary>
    public event Action? DuplicateRequested;

    /// <summary>Gets whether the camera is being flown.</summary>
    public bool IsFlying => _camera.Drag != CameraDrag.None;

    // The axes the gizmos use: the world's, or the selected object's own when local space is on (not for graph nodes).
    private Matrix3f GizmoFrame =>
        EditorCVars.LocalSpace.Value && _graph.PickedNode is null && _workspace.Selection.Primary is { } task
            ? TaskPresentation.OrientationMatrix(task)
            : Matrix3f.Identity;

    private static bool ShowGizmos => EditorCVars.ShowGizmos.Value;

    // The snap steps the gizmos use, from the editor's settings (the viewport toolbar sets them).
    private void ApplySnapSettings()
    {
        double grid = EditorCVars.GridSnap.Value ? EditorCVars.GridStep.Value : 0.0;
        _gizmo.Snap = grid;
        _box.Snap = grid;
        _rotate.SnapDegrees = EditorCVars.AngleSnap.Value ? EditorCVars.AngleStep.Value : 0.0;
        _rotate.UpOnly = _workspace.Selection.Primary is { } task && TaskPresentation.TurnsAboutUpOnly(task);
        _camera.SensitivityScale = EditorCVars.MouseSensitivity.Value;
        _camera.FlySmoothing = EditorCVars.CameraSmoothing.Value;
        _camera.InvertPan = EditorCVars.InvertPan.Value;
        GizmoSize.Scale = EditorCVars.GizmoScale.Value * Theme.Scale;
    }

    /// <summary>Gets or sets the gizmo shown on the selection.</summary>
    public ViewportTool Tool { get; set; } = ViewportTool.Move;

    private bool Resizing => Tool == ViewportTool.Scale && _graph.PickedNode is null && SelectedBox is not null;

    // The selected task's box, when it is a volume with a constant place and size.
    private BoxGizmo.Box? SelectedBox =>
        _workspace.Selection.Primary is { } task && TaskVolume.Of(task) is { } volume
        && volume.HalfMetres() is { } half && TaskPresentation.PositionMetres(task) is { } centre
            ? new BoxGizmo.Box(centre, TaskPresentation.OrientationMatrix(task), half)
            : null;

    private bool Rotating => Tool == ViewportTool.Rotate && _graph.PickedNode is null
        && _workspace.Selection.Primary is { } task && TaskPresentation.Orientation(task) is not null;

    // The selected AI graph and its document, when the selection is one; picking another task forgets the picked node.
    private (MissionTask Task, GraphDocument Graph)? ShownGraph
    {
        get
        {
            MissionTask? selected = _workspace.Selection.Primary;
            if (!ReferenceEquals(selected, _graphTask))
            {
                _graphTask = selected;
                _graph.Unpick();
            }

            return selected is not null && _workspace.Mission is { } mission && _workspace.Graphs.Get(mission, selected) is { } graph
                ? (selected, graph)
                : null;
        }
    }

    // Where the gizmo stands: on the picked graph node, else on the selected task.
    private Vector3d? GizmoOrigin =>
        ShownGraph is { } shown && _graph.PickedPosition(shown.Task, shown.Graph) is { } node
            ? node
            : _workspace.Selection.Primary is not { } task ? null
            : MissionGraphs.IsGraph(task) && TaskPresentation.PositionMetres(task) is not null ? MissionGraphs.OriginMetres(task)
            : TaskPresentation.PositionMetres(task);

    /// <inheritdoc/>
    public override bool Focusable => true;

    private bool ShowsWorld => _workspace.Mission is not null && _host.Camera is not null;

    private ViewportProjector Projector => new(_host.View);

    /// <summary>Moves the camera to look at a task.</summary>
    /// <param name="task">The task.</param>
    public void Frame(MissionTask task)
    {
        if (_host.Camera is not { } camera || TaskPresentation.PositionMetres(task) is not { } target)
        {
            return;
        }

        // What it drew is framed whole, middle and height, not the point it stands on: a tower framed by its foot showed its
        // legs and lost its tank.
        double radius = _live.RadiusMetres(task) ?? 2.0;
        BoxGizmo.Box[] drawn = [.. _live.Boxes(task)];
        if (drawn.Length > 0)
        {
            Vector3d sum = Vector3d.Zero;
            foreach (BoxGizmo.Box box in drawn)
            {
                sum += box.Centre;
            }

            target = sum / drawn.Length;
            radius = Math.Max(radius, drawn.Max(static b => b.Half.Length));
        }

        // A graph is framed by its nodes, which can stand far from its own position.
        if (_workspace.Mission is { } mission && _workspace.Graphs.Get(mission, task) is { } graph
            && MissionGraphs.BoundsMetres(task, graph) is { } bounds)
        {
            (target, radius) = (bounds.Centre, bounds.Radius);
        }

        // A glide to where all of it is in view along the current heading, which then orbits around it.
        _camera.Focus(camera, target, radius);
    }

    /// <summary>
    /// Puts the camera over a player's shoulder: behind and above them, looking past them towards where they are headed, as a
    /// new mission opens.
    /// </summary>
    /// <param name="player">Where the player stands, in world metres.</param>
    /// <param name="ahead">A point they are headed for (an exit), or null to look along the camera's heading.</param>
    public void LookOver(Vector3d player, Vector3d? ahead)
    {
        if (_host.Camera is not { } camera)
        {
            return;
        }

        Vector3d along = ahead is { } to ? to - player : EditorCamera.Axes(camera.Yaw, 0f).Forward;
        along = new Vector3d(along.X, along.Y, 0.0);
        along = along.Length > 1e-6 ? along / along.Length : new Vector3d(0.0, 1.0, 0.0);
        Vector3d target = player + (along * 8.0) + new Vector3d(0.0, 0.0, 1.0);
        _camera.Place(camera, player - (along * 14.0) + new Vector3d(0.0, 0.0, 7.0), target);
    }

    /// <summary>Picks what is under the middle of the view, as a click there would.</summary>
    public void PickAtCentre() => Pick(WorldRect.Center, toggle: false);

    /// <summary>Paints the terrain brush at the middle of the view for a while, as one stroke.</summary>
    /// <param name="seconds">How long to hold it.</param>
    /// <param name="inverted">Whether to do the opposite, as Shift does.</param>
    /// <returns>Whether there was ground there to paint.</returns>
    public bool SculptAtCentre(double seconds, bool inverted = false)
    {
        if (!_terrain.Begin(Projector, WorldRect.Center, inverted))
        {
            return false;
        }

        for (double left = seconds; left > 0.0; left -= 0.1)
        {
            _terrain.Tick((float)Math.Min(0.1, left));
        }

        _terrain.End();
        return true;
    }

    /// <summary>Moves the camera for a frame: a held look, pan or orbit, and any glide under way. Called once per frame.</summary>
    /// <param name="elapsedSeconds">The frame time.</param>
    public void UpdateCamera(float elapsedSeconds)
    {
        if (_host.Camera is not { } camera)
        {
            return;
        }

        MouseButton? held = _camera.Drag switch
        {
            CameraDrag.Look => MouseButton.Right,
            CameraDrag.Pan => MouseButton.Middle,
            CameraDrag.Orbit => MouseButton.Left,
            _ => null,
        };
        if (held is { } button && !_host.Input.Mouse.IsDown(button))
        {
            _camera.End();
            _host.CursorLocked = false;
        }
        else if (held is not null)
        {
            _host.CursorLocked = true;
        }

        _camera.Update(camera, _host.Input, elapsedSeconds);
    }

    // The nearest surface under a point of the view, the ground or a model other than the one being moved: where the move
    // gizmo's centre puts what it carries.
    private Vector3d? SurfaceUnder(UiVec2 point, MissionTask? moving)
    {
        ViewportProjector projector = Projector;
        if (!projector.TryRay(point, out Vector3d ray))
        {
            return null;
        }

        double nearest = double.MaxValue;
        if (_live.Pick(projector.Eye, ray, PickReachMetres, out double meshDistance, ignore: moving) is not null)
        {
            nearest = meshDistance;
        }

        if (_host.Terrain is { } terrain && terrain.TryRaycast(projector.Eye, ray, PickReachMetres, out Vector3d ground))
        {
            nearest = Math.Min(nearest, (ground - projector.Eye).Length);
        }

        return nearest < double.MaxValue ? projector.Eye + (ray * nearest) : null;
    }

    /// <summary>Gets where a new object goes: what is in the middle of the view, or a little in front of the camera.</summary>
    /// <returns>The point in world metres, or <see langword="null"/> with no camera.</returns>
    public Vector3d? PlacementPoint()
    {
        if (_host.Camera is not { } camera)
        {
            return null;
        }

        AimPivot();
        (Vector3d forward, _, _) = EditorCamera.Axes(camera.Yaw, camera.Pitch);
        return camera.Position + (forward * Math.Min(_camera.PivotDistance, 60.0));
    }

    // The distance to what is under the middle of the view, which orbiting turns around and the wheel dollies toward.
    private void AimPivot()
    {
        ViewportProjector projector = Projector;
        if (!projector.TryRay(WorldRect.Center, out Vector3d ray))
        {
            return;
        }

        double nearest = double.MaxValue;
        if (_live.Pick(projector.Eye, ray, PickReachMetres, out double meshDistance) is not null)
        {
            nearest = meshDistance;
        }

        if (_host.Terrain is { } terrain && terrain.TryRaycast(projector.Eye, ray, PickReachMetres, out Vector3d ground))
        {
            nearest = Math.Min(nearest, (ground - projector.Eye).Length);
        }

        if (nearest < double.MaxValue)
        {
            _camera.PivotDistance = Math.Max(0.5, nearest);
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <summary>Gets or sets the toolbar along the viewport's top.</summary>
    public UI.Editor.Widgets.Toolbar? Overlay
    {
        get;
        set
        {
            if (field is not null)
            {
                RemoveChild(field);
            }

            field = value;
            if (value is not null)
            {
                AddChild(value);
            }
        }
    }

    /// <summary>Gets the part of the viewport the world is drawn in: all of it below the toolbar.</summary>
    public UiRect WorldRect =>
        Overlay is { Visibility: not Visibility.Collapsed } && ShowsWorld
            ? UiRect.FromEdges(Bounds.X, Bounds.Y + Theme.ToolbarHeight, Bounds.Right, Bounds.Bottom)
            : Bounds;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        if (Overlay is { } overlay)
        {
            overlay.Visibility = ShowsWorld ? Visibility.Visible : Visibility.Collapsed;
            overlay.Place(context, new UiRect(contentRect.X, contentRect.Y, contentRect.Width, Theme.ToolbarHeight));
        }
    }

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        if (!ShowsWorld)
        {
            if (e.Button == MouseButton.Left && MissionAt(e.Position) is { } index)
            {
                _host.OpenMission(_host.Missions[index].Id);
            }

            return false;
        }

        // The orientation widget: a dot looks along its axis, anywhere else on it orbits.
        if (e.Button == MouseButton.Left && _host.Camera is { } viewCamera && OrientationArea.Contains(e.Position))
        {
            Focus();
            AimPivot();
            int dot = OrientationGizmo.DotAt(OrientationArea, viewCamera.Yaw, viewCamera.Pitch, e.Position);
            if (dot >= 0)
            {
                _camera.LookAlong(viewCamera, OrientationGizmo.LookFor(dot));
                return true;
            }

            _camera.Begin(CameraDrag.Orbit);
            return true;
        }

        if (PatrolMenu(e))
        {
            return true;
        }

        if (e.Button is MouseButton.Right or MouseButton.Middle || (e.Button == MouseButton.Left && e.Alt))
        {
            Focus();
            if (e.Button != MouseButton.Right)
            {
                AimPivot();
            }

            _camera.Begin(e.Button switch { MouseButton.Right => CameraDrag.Look, MouseButton.Middle => CameraDrag.Pan, _ => CameraDrag.Orbit });
            return true;
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        Focus();
        if (Sculpting)
        {
            return _terrain.Begin(Projector, e.Position, e.Shift);
        }

        if (PatrolPress(e))
        {
            return true;
        }

        if (Resizing && SelectedBox is { } box && _box.Begin(Projector, box, e.Position))
        {
            return true;
        }

        ApplySnapSettings();
        if (ShowGizmos && _workspace.Selection.Primary is { } selected && GizmoOrigin is { } origin && !Resizing
            && (Rotating
                ? _rotate.Begin(Projector, origin, GizmoFrame, TaskPresentation.OrientationMatrix(selected), e.Position)
                : _gizmo.Begin(Projector, origin, GizmoFrame, e.Position, _graph.PickedNode is null ? point => SurfaceUnder(point, selected) : null)))
        {
            // Shift on a move handle duplicates first and drags the copies, which stand where the originals did.
            if (e.Shift && _gizmo.IsDragging && _graph.PickedNode is null)
            {
                DuplicateRequested?.Invoke();
            }

            _moveStarts.Clear();
            foreach (MissionTask task in _workspace.Selection.Items)
            {
                if (TaskPresentation.PositionMetres(task) is { } at)
                {
                    _moveStarts[task] = at;
                }
            }

            // What is placed under, inside or on the selection goes with it (ed_move_contents).
            IReadOnlyList<MissionTask> carried = _workspace.Document is { } carrying && _graph.PickedNode is null
                ? ObjectCarry.Carried(carrying, _live, [.. _workspace.Selection.Items])
                : [];
            foreach (MissionTask task in carried)
            {
                if (TaskPresentation.PositionMetres(task) is { } at)
                {
                    _moveStarts.TryAdd(task, at);
                }
            }

            _turnCarried = Rotating ? ObjectCarry.Snapshot(carried) : [];
            return true;
        }

        if (ShownGraph is { } shown)
        {
            // Ctrl+click on another node, with one picked, links the two (or unlinks them) and moves on to it, so a path
            // is laid node by node.
            if (e.Control && _graph.PickedNode is { } from
                && GraphOverlay.NodeAt(Projector, shown.Task, shown.Graph, e.Position, Theme.Px(10f)) is { } to && to != from)
            {
                if (_workspace.Graphs.ToggleLink(shown.Task, from, to) is { } link)
                {
                    _workspace.Execute(link);
                }

                _graph.Pick(to);
                return true;
            }

            if (!e.Control && _graph.Pick(Projector, shown.Task, shown.Graph, e.Position, Theme.Px(10f)))
            {
                return true;
            }
        }

        Pick(e.Position, e.Control);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMove(in PointerEvent e)
    {
        if (!ShowsWorld)
        {
            _hoveredMission = MissionAt(e.Position) ?? -1;
            return;
        }

        if (Sculpting)
        {
            _terrain.Hover(Projector, e.Position);
            return;
        }

        if (PatrolDrag(e))
        {
            return;
        }

        if (_host.Camera is { } hoverCamera)
        {
            _orientation.Hover(OrientationArea, hoverCamera.Yaw, hoverCamera.Pitch, e.Position);
        }

        // What a click would pick, outlined while the pointer is over it; nothing while a drag or the camera has the mouse.
        bool busy = IsFlying || _gizmo.IsDragging || _rotate.IsDragging || _box.IsDragging;
        _hovered = !busy && Projector.TryRay(e.Position, out Vector3d ray)
            ? _live.Pick(Projector.Eye, ray, PickReachMetres, out _)
            : null;

        MissionTask? selected = _workspace.Selection.Primary;
        if (selected is null || GizmoOrigin is not { } origin)
        {
            return;
        }

        if (_gizmo.IsDragging && _graph.PickedNode is { } nodeId && ShownGraph is { } shown)
        {
            Vector3d to = _gizmo.Drag(Projector, e.Position, e.Control);
            if (_workspace.Graphs.MoveNode(shown.Task, nodeId, MissionGraphs.ToLocalUnits(shown.Task, to)) is { } move)
            {
                _workspace.Execute(move);
            }

            return;
        }

        if (_box.IsDragging && _workspace.Document is { } resized && TaskVolume.Of(selected) is { } volume
            && TaskPresentation.Position(selected) is { } centreParameter)
        {
            (Vector3d centre, Vector3d half) = _box.Drag(e.Position, e.Shift);
            _workspace.Execute(new CompositeCommand("Resize", [
                SetParameterCommand.Numbers(resized, selected, volume.Parameter.Name, volume.Stored(half)),
                SetParameterCommand.Numbers(resized, selected, centreParameter.Name, [WorldUnits.FromMetres(centre.X), WorldUnits.FromMetres(centre.Y), WorldUnits.FromMetres(centre.Z)]),
            ]));
            return;
        }

        if (_rotate.IsDragging && _workspace.Document is { } turning && TaskPresentation.Orientation(selected) is { } orientation)
        {
            Vector3f angles = _rotate.Drag(e.Position).ToEngineEulerAngles();
            SetParameterCommand turn = TaskPresentation.TurnsAboutUpOnly(selected)
                ? SetParameterCommand.Numbers(turning, selected, orientation.Name, [angles.Z])
                : SetParameterCommand.Numbers(turning, selected, orientation.Name, [angles.X, angles.Y, angles.Z]);
            _workspace.Execute(_turnCarried.Count == 0
                ? turn
                : new CompositeCommand(turn.Name, [turn, .. ObjectCarry.Turn(turning, _turnCarried, origin, _rotate.WorldTurn)]));
            return;
        }

        if (_gizmo.IsDragging && _workspace.Document is { } document && TaskPresentation.Position(selected) is not null)
        {
            // Every selected object moves by what the primary one moved, as one step.
            Vector3d moved = _gizmo.Drag(Projector, e.Position, e.Control);
            Vector3d by = _moveStarts.TryGetValue(selected, out Vector3d primaryStart) ? moved - primaryStart : Vector3d.Zero;
            List<IEditorCommand> moves = [];
            foreach ((MissionTask task, Vector3d start) in _moveStarts)
            {
                if (TaskPresentation.Position(task) is not { } position)
                {
                    continue;
                }

                Vector3d to = ReferenceEquals(task, selected) ? moved : start + by;
                if (MissionGraphs.IsGraph(task))
                {
                    // Through the graph's parent frame, back to the value its parameter holds (in metres, as below).
                    to = MissionGraphs.OffsetForOrigin(task, to) / WorldUnits.PerMetre;
                }

                moves.Add(SetParameterCommand.Numbers(document, task, position.Name, [WorldUnits.FromMetres(to.X), WorldUnits.FromMetres(to.Y), WorldUnits.FromMetres(to.Z)]));
            }

            _workspace.Execute(moves.Count == 1 ? moves[0] : new CompositeCommand("Move", moves));
            return;
        }

        if (Resizing && SelectedBox is { } hovered)
        {
            _box.Hover(Projector, hovered, e.Position);
        }
        else if (Rotating)
        {
            _rotate.Hover(Projector, origin, GizmoFrame, e.Position);
        }
        else
        {
            _gizmo.Hover(Projector, origin, GizmoFrame, e.Position);
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerOverChanged(bool over)
    {
        if (!over)
        {
            _hovered = null;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerUp(in PointerEvent e)
    {
        _terrain.End();
        Patrol?.Release();
        if (_gizmo.IsDragging || _rotate.IsDragging || _box.IsDragging)
        {
            _gizmo.End();
            _rotate.End();
            _box.End();
            _workspace.History.BreakMerge();
        }
    }

    /// <inheritdoc/>
    protected override void OnCaptureLost()
    {
        _terrain.Cancel();
        if (_gizmo.IsDragging || _rotate.IsDragging || _box.IsDragging)
        {
            // Escape mid-drag puts the object back the way the drag found it.
            _gizmo.End();
            _rotate.End();
            _box.End();
            _workspace.History.Undo();
        }
    }

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        if (!ShowsWorld || _host.Camera is not { } camera)
        {
            return false;
        }

        // While flying the wheel sets the speed; otherwise it dollies toward what is in the middle of the view.
        if (_camera.Drag == CameraDrag.Look)
        {
            _camera.ChangeSpeed(camera, notches);
        }
        else
        {
            AimPivot();
            _camera.Dolly(camera, notches);
        }

        return true;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(in KeyEvent e)
    {
        // While flying, the movement keys belong to the camera, not to the tool shortcuts.
        if (IsFlying && Array.IndexOf(FlightKeys, e.Key) >= 0)
        {
            return true;
        }

        if (PatrolKey(e))
        {
            return true;
        }

        if (Sculpting && e.Modifiers == KeyModifiers.None && e.Key is KeyCode.LeftBracket or KeyCode.RightBracket)
        {
            _terrain.Resize(larger: e.Key == KeyCode.RightBracket);
            return true;
        }

        if (e.Key == KeyCode.F && e.Modifiers == KeyModifiers.None && _workspace.Selection.Primary is { } selected)
        {
            Frame(selected);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (!ShowsWorld)
        {
            DrawStartPage(painter);
            return;
        }

        painter.PushClip(Bounds);
        ApplySnapSettings();
        DrawGrid(painter);
        DrawDropMarker(painter);
        if (_host.Camera is { } orientationCamera)
        {
            bool overWidget = IsPointerOver && OrientationArea.Contains(Root?.PointerPosition ?? default);
            _orientation.Draw(painter, Theme, OrientationArea, orientationCamera.Yaw, orientationCamera.Pitch, overWidget);
        }

        if (!ShowGizmos)
        {
            DrawChips(painter);
            painter.PopClip();
            return;
        }

        if (ShownGraph is { } shown)
        {
            _graph.Draw(painter, Theme, Projector, shown.Task, shown.Graph);
        }

        if (Sculpting)
        {
            _terrain.Draw(painter, Theme, Projector);
        }

        DrawPatrol(painter);

        if (SelectedBox is { } box && _graph.PickedNode is null)
        {
            _box.Draw(painter, Theme, Projector, box, handles: Resizing);
        }

        if (_workspace.Selection.Primary is not null && GizmoOrigin is { } origin && !Resizing && !Sculpting)
        {
            if (Rotating)
            {
                _rotate.Draw(painter, Theme, Projector, origin, GizmoFrame);
            }
            else
            {
                _gizmo.Draw(painter, Theme, Projector, origin, GizmoFrame);
                if (Tool == ViewportTool.Rotate && _graph.PickedNode is null && Projector.TryProject(origin, out UiVec2 at))
                {
                    // The rotate tool on something with no orientation: say so rather than quietly showing the move gizmo.
                    const string hint = "This object has no orientation to turn";
                    float width = Theme.SmallFont.MeasureWidth(hint, Theme.SmallTextSize) + Theme.Px(12f);
                    UiRect note = new(MathF.Round(at.X - (width * 0.5f)), MathF.Round(at.Y + Theme.Px(40f)), width, Theme.ControlHeight);
                    EditorDraw.Fill(painter, Theme, note, Bgra32.FromRgba(0, 0, 0, 170));
                    painter.DrawText(Theme.SmallFont, hint, new UiVec2(note.X + Theme.Px(6f), MathF.Round(note.Y + ((note.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f))), Theme.SmallTextSize, Theme.Text);
                }
            }
        }

        DrawOutlines(painter);
        DrawChips(painter);
        painter.PopClip();
        if (IsFocused)
        {
            EditorDraw.Frame(painter, Theme, Bounds, Bgra32.Transparent, Theme.Accent.WithAlpha(120), 0f);
        }
    }

    private void Pick(UiVec2 point, bool toggle)
    {
        // What is drawn under the click: the first model the ray through it meets. A task with nothing drawn (a light, an
        // area, a graph) is picked by its point instead, within a small radius on screen -- when that point is nearer the
        // eye than the model the ray met, so a marker in front of a building wins and one behind it does not.
        ViewportProjector projector = Projector;
        double meshDistance = double.MaxValue;
        MissionTask? best = projector.TryRay(point, out Vector3d ray)
            ? _live.Pick(projector.Eye, ray, PickReachMetres, out meshDistance)
            : null;

        float nearestOnScreen = Theme.Px(14f);
        foreach (MissionTask task in _workspace.Document?.EnumerateTasks() ?? [])
        {
            if (TaskPresentation.PositionMetres(task) is { } at && projector.TryProject(at, out UiVec2 screen))
            {
                float distance = UiVec2.Distance(screen, point);
                if (distance < nearestOnScreen && (at - projector.Eye).Length < meshDistance)
                {
                    nearestOnScreen = distance;
                    best = task;
                }
            }
        }

        if (toggle && best is not null)
        {
            _workspace.Selection.Toggle(best);
        }
        else
        {
            _workspace.Selection.Set(best);
        }
    }

}
