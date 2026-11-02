using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
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

/// <summary>
/// The world view. Hold the right mouse button to look and fly (W A S D, Q E, Shift faster), the middle button to
/// pan, the wheel to dolly; click to select, drag the gizmo to move, F to frame the selection. With no mission
/// open it is the start page that opens one.
/// </summary>
internal sealed class ViewportPanel : EditorElement
{
    private static readonly KeyCode[] FlightKeys = [KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.LeftShift];

    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly LiveWorldLink _live;
    private readonly TranslateGizmo _gizmo = new();
    private readonly List<InspectableEntity> _entities = [];
    private MouseButton? _flying;
    private int _hoveredMission = -1;

    public ViewportPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace, LiveWorldLink live)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _live = live;
    }

    /// <summary>Gets whether the camera is being flown.</summary>
    public bool IsFlying => _flying is not null;

    /// <summary>Gets or sets the gizmo's snap step in metres; zero for none.</summary>
    public double Snap
    {
        get => _gizmo.Snap;
        set => _gizmo.Snap = value;
    }

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

        // Backed off along the current heading by enough to see all of it, a little above and looking down, so the
        // object fills the middle of the view whatever its size.
        double radius = _live.RadiusMetres(task) ?? 2.0;
        double distance = Math.Clamp(radius * 2.4, 3.0, 400.0);
        Vector3d back = new(-Math.Sin(camera.Yaw) * Math.Cos(0.35), -Math.Cos(camera.Yaw) * Math.Cos(0.35), Math.Sin(0.35));
        camera.MoveTo(target + (back * distance), camera.Yaw, -0.35f);
    }

    /// <summary>Flies the camera while a fly button is held. Called once per frame by the session.</summary>
    /// <param name="elapsedSeconds">The frame time.</param>
    public void FlyCamera(float elapsedSeconds)
    {
        if (_flying is { } button && _host.Camera is { } camera)
        {
            if (!_host.Input.Mouse.IsDown(button))
            {
                _flying = null;
                _host.CursorLocked = false;
                return;
            }

            _host.CursorLocked = true;
            camera.Update(_host.Input, elapsedSeconds);
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

        if (e.Button is MouseButton.Right or MouseButton.Middle)
        {
            Focus();
            _flying = e.Button;
            return true;
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        Focus();
        if (_workspace.Selection.Primary is { } selected && TaskPresentation.PositionMetres(selected) is { } origin && _gizmo.Begin(Projector, origin, e.Position))
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

        MissionTask? selected = _workspace.Selection.Primary;
        if (selected is null || TaskPresentation.PositionMetres(selected) is not { } origin)
        {
            return;
        }

        if (_gizmo.IsDragging && _workspace.Document is { } document && TaskPresentation.Position(selected) is { } position)
        {
            Vector3d moved = _gizmo.Drag(Projector, e.Position, e.Shift);
            _workspace.Execute(SetParameterCommand.Numbers(
                document, selected, position.Name, [WorldUnits.FromMetres(moved.X), WorldUnits.FromMetres(moved.Y), WorldUnits.FromMetres(moved.Z)]));
            return;
        }

        _gizmo.Hover(Projector, origin, e.Position);
    }

    /// <inheritdoc/>
    protected override void OnPointerUp(in PointerEvent e)
    {
        if (_gizmo.IsDragging)
        {
            _gizmo.End();
            _workspace.History.BreakMerge();
        }
    }

    /// <inheritdoc/>
    protected override void OnCaptureLost()
    {
        if (_gizmo.IsDragging)
        {
            // Escape mid-drag puts the object back where the drag began.
            _gizmo.End();
            _workspace.History.Undo();
        }
    }

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        if (!ShowsWorld || _host.Camera is not { } camera || IsFlying)
        {
            return false;
        }

        // Dolly along the view, faster the further the camera has to go.
        Vector3d forward = new(Math.Sin(camera.Yaw) * Math.Cos(camera.Pitch), Math.Cos(camera.Yaw) * Math.Cos(camera.Pitch), Math.Sin(camera.Pitch));
        double step = Math.Max(0.5, camera.Speed * 0.25) * notches;
        camera.MoveTo(camera.Position + (forward * step), camera.Yaw, camera.Pitch);
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
        if (_workspace.Selection.Primary is { } selected && TaskPresentation.PositionMetres(selected) is { } origin)
        {
            _gizmo.Draw(painter, Theme, Projector, origin);
        }

        DrawChips(painter);
        painter.PopClip();
        if (IsFocused)
        {
            EditorDraw.Frame(painter, Theme, Bounds, Bgra32.Transparent, Theme.Accent.WithAlpha(120), 0f);
        }
    }


    private void Pick(UiVec2 point, bool toggle)
    {
        // Nearest placed task to the click, on screen, within a small radius; tasks without a place are picked in the
        // outliner instead.
        MissionTask? best = null;
        float bestDistance = Theme.Px(14f);
        ViewportProjector projector = Projector;
        foreach (MissionTask task in _workspace.Document?.EnumerateTasks() ?? [])
        {
            if (TaskPresentation.PositionMetres(task) is { } at && projector.TryProject(at, out UiVec2 screen))
            {
                float distance = UiVec2.Distance(screen, point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
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
            painter.DrawText(Theme.SmallFont, speed, new UiVec2(right.X + Theme.Px(8f), MathF.Round(right.Y + ((right.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f))), Theme.SmallTextSize, Theme.TextMuted);
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
