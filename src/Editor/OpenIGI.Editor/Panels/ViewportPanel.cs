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
using OpenIGI.Presentation.Cameras;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
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
}

/// <summary>
/// The world view. Hold the right mouse button to look and fly (W A S D, Q E, Shift faster), the middle button to
/// pan, the wheel to dolly; click to select, drag the gizmo to move, F to frame the selection. With no mission
/// open it is the start page that opens one.
/// </summary>
internal sealed class ViewportPanel : EditorElement
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
    private readonly List<InspectableEntity> _entities = [];
    private readonly EditorCamera _camera = new();
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

    /// <summary>Gets whether the camera is being flown.</summary>
    public bool IsFlying => _camera.Drag != CameraDrag.None;

    /// <summary>Gets or sets the gizmo's snap step in metres; zero for none. Snapping also turns in 15° steps.</summary>
    public double Snap
    {
        get => _gizmo.Snap;
        set
        {
            _gizmo.Snap = value;
            _box.Snap = value;
            _rotate.Snap = value > 0;
        }
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

        // A graph is framed by its nodes, which can stand far from its own position.
        double radius = _live.RadiusMetres(task) ?? 2.0;
        if (_workspace.Mission is { } mission && _workspace.Graphs.Get(mission, task) is { } graph
            && MissionGraphs.BoundsMetres(task, graph) is { } bounds)
        {
            (target, radius) = (bounds.Centre, bounds.Radius);
        }

        // A glide to where all of it is in view along the current heading, which then orbits around it.
        _camera.Focus(camera, target, radius);
    }

    /// <summary>Picks what is under the middle of the view, as a click there would.</summary>
    public void PickAtCentre() => Pick(Bounds.Center, toggle: false);

    /// <summary>Paints the terrain brush at the middle of the view for a while, as one stroke.</summary>
    /// <param name="seconds">How long to hold it.</param>
    /// <returns>Whether there was ground there to paint.</returns>
    public bool SculptAtCentre(double seconds)
    {
        if (!_terrain.Begin(Projector, Bounds.Center, inverted: false))
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

    // Boxes around what is selected, steady, and around what the pointer is over, pulsing, as scene editors show them.
    private void DrawOutlines(UiPainter painter)
    {
        ViewportProjector projector = Projector;
        Bgra32 selectedColour = Theme.Accent.WithAlpha(200);
        foreach (MissionTask task in _workspace.Selection.Items)
        {
            foreach (BoxGizmo.Box box in _live.Boxes(task))
            {
                BoxGizmo.DrawEdges(painter, Theme, projector, box, selectedColour);
            }
        }

        if (_hovered is { } hovered && !_workspace.Selection.Contains(hovered) && !Sculpting)
        {
            double pulse = 0.5 + (0.5 * Math.Sin(Environment.TickCount64 / 1000.0 * 8.0));
            Bgra32 hoverColour = Bgra32.FromRgb(255, 222, 90).WithAlpha((byte)(90 + (110 * pulse)));
            foreach (BoxGizmo.Box box in _live.Boxes(hovered))
            {
                BoxGizmo.DrawEdges(painter, Theme, projector, box, hoverColour);
            }
        }
    }

    // The nearest surface under a point of the view, the ground or a model other than the one being moved: where the move
    // gizmo's centre puts what it carries.
    private Vector3d? SurfaceUnder(UiVec2 point, MissionTask moving)
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

    // The distance to what is under the middle of the view, which orbiting turns around and the wheel dollies toward.
    private void AimPivot()
    {
        ViewportProjector projector = Projector;
        if (!projector.TryRay(Bounds.Center, out Vector3d ray))
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

        if (Resizing && SelectedBox is { } box && _box.Begin(Projector, box, e.Position))
        {
            return true;
        }

        if (_workspace.Selection.Primary is { } selected && GizmoOrigin is { } origin && !Resizing
            && (Rotating
                ? _rotate.Begin(Projector, origin, TaskPresentation.OrientationMatrix(selected), e.Position)
                : _gizmo.Begin(Projector, origin, e.Position, _graph.PickedNode is null ? point => SurfaceUnder(point, selected) : null)))
        {
            return true;
        }

        if (ShownGraph is { } shown && !e.Control && _graph.Pick(Projector, shown.Task, shown.Graph, e.Position, Theme.Px(10f)))
        {
            return true;
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
            _workspace.Execute(SetParameterCommand.Numbers(turning, selected, orientation.Name, [angles.X, angles.Y, angles.Z]));
            return;
        }

        if (_gizmo.IsDragging && _workspace.Document is { } document && TaskPresentation.Position(selected) is { } position)
        {
            Vector3d moved = _gizmo.Drag(Projector, e.Position, e.Control);
            if (MissionGraphs.IsGraph(selected))
            {
                // Through the graph's parent frame, back to the value its parameter holds (in metres, as below).
                moved = MissionGraphs.OffsetForOrigin(selected, moved) / WorldUnits.PerMetre;
            }

            _workspace.Execute(SetParameterCommand.Numbers(
                document, selected, position.Name, [WorldUnits.FromMetres(moved.X), WorldUnits.FromMetres(moved.Y), WorldUnits.FromMetres(moved.Z)]));
            return;
        }

        if (Resizing && SelectedBox is { } hovered)
        {
            _box.Hover(Projector, hovered, e.Position);
        }
        else if (Rotating)
        {
            _rotate.Hover(Projector, origin, e.Position);
        }
        else
        {
            _gizmo.Hover(Projector, origin, e.Position);
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
        if (ShownGraph is { } shown)
        {
            _graph.Draw(painter, Theme, Projector, shown.Task, shown.Graph);
        }

        if (Sculpting)
        {
            _terrain.Draw(painter, Theme, Projector);
        }

        if (SelectedBox is { } box && _graph.PickedNode is null)
        {
            _box.Draw(painter, Theme, Projector, box, handles: Resizing);
        }

        if (_workspace.Selection.Primary is not null && GizmoOrigin is { } origin && !Resizing && !Sculpting)
        {
            if (Rotating)
            {
                _rotate.Draw(painter, Theme, Projector, origin);
            }
            else
            {
                _gizmo.Draw(painter, Theme, Projector, origin);
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

    private void DrawChips(UiPainter painter)
    {
        string mode = _workspace.Mission is not null ? $"{_host.Title.DisplayName}  •  {_workspace.MissionName}" : string.Empty;
        UiRect chip = new(Bounds.X + Theme.Px(10f), Bounds.Y + Theme.Px(10f), Theme.Font.MeasureWidth(mode, Theme.TextSize) + Theme.Px(20f), Theme.ControlHeight);
        EditorDraw.Fill(painter, Theme, chip, Theme.Background.WithAlpha(170));
        EditorDraw.Label(painter, Theme, mode, chip.Deflate(new Thickness(Theme.Px(10f), 0f)), Theme.Text);

        if (_host.Camera is { } camera)
        {
            string speed = FormattableString.Invariant($"Camera {camera.Speed:0.#} m/s");
            float width = Theme.SmallFont.MeasureWidth(speed, Theme.SmallTextSize) + Theme.Px(16f);
            UiRect right = new(Bounds.Right - width - Theme.Px(10f), Bounds.Y + Theme.Px(10f), width, Theme.ControlHeight);
            EditorDraw.Fill(painter, Theme, right, Theme.Background.WithAlpha(170));
            painter.DrawText(Theme.SmallFont, speed, new UiVec2(right.X + Theme.Px(8f), MathF.Round(right.Y + ((right.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f))), Theme.SmallTextSize,
                _camera.SecondsSinceSpeedChange < 1.5 ? Theme.Accent : Theme.TextMuted);
        }

        if (_host.IsWarmingUp)
        {
            EditorDraw.Label(painter, Theme, "Loading…", new UiRect(Bounds.Center.X - Theme.Px(30f), Bounds.Center.Y, Theme.Px(80f), Theme.RowHeight), Theme.TextMuted);
        }
    }

    private UiRect StartList
    {
        get
        {
            float width = MathF.Min(Theme.Px(520f), Bounds.Width - Theme.Px(40f));
            float top = Bounds.Y + MathF.Max(Theme.Px(40f), (Bounds.Height * 0.18f));
            return new UiRect(MathF.Round(Bounds.Center.X - (width * 0.5f)), top + Theme.Px(70f), width, Bounds.Bottom - top - Theme.Px(100f));
        }
    }

    private int? MissionAt(UiVec2 point)
    {
        UiRect list = StartList;
        if (!list.Contains(point))
        {
            return null;
        }

        int index = (int)((point.Y - list.Y) / (Theme.RowHeight + Theme.Px(6f)));
        return index >= 0 && index < _host.Missions.Count ? index : null;
    }

    private void DrawStartPage(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Background);
        UiRect list = StartList;
        float y = list.Y - Theme.Px(70f);
        EditorDraw.Label(painter, Theme, $"Open a {_host.Title.DisplayName} mission", new UiRect(list.X, y, list.Width, Theme.Px(30f)), Theme.Text, strong: true);
        EditorDraw.Label(painter, Theme, "Edits are saved to a project of their own; the game's files are never changed.", new UiRect(list.X, y + Theme.Px(28f), list.Width, Theme.RowHeight), Theme.TextMuted);

        painter.PushClip(list);
        float rowHeight = Theme.RowHeight + Theme.Px(6f);
        for (int i = 0; i < _host.Missions.Count; i++)
        {
            MissionDescriptor mission = _host.Missions[i];
            UiRect row = new(list.X, list.Y + (i * rowHeight), list.Width, rowHeight - Theme.Px(4f));
            EditorDraw.Fill(painter, Theme, row, i == _hoveredMission ? Theme.Selection : Theme.Panel);
            EditorDraw.Icon(painter, Theme, KitIcon.Terrain, new UiRect(row.X + Theme.Px(8f), row.Y, Theme.IconSize, row.Height));
            EditorDraw.Label(painter, Theme, FormattableString.Invariant($"{mission.Id}.  {_host.MissionName(mission)}"), new UiRect(row.X + Theme.Px(32f), row.Y, row.Width - Theme.Px(40f), row.Height), Theme.Text);
        }

        painter.PopClip();
    }
}
