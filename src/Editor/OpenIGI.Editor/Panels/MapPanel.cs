using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The mission from straight above: the level as the game's own collision sees it, every object of the mission on it as a
/// mark, the areas as the boxes they are. Click to select, drag to move (an object keeps its height over whatever it stands
/// on), wheel to zoom, right or middle drag to pan, double-click to look at it in the viewport.
/// </summary>
/// <remarks>
/// The picture is made once a mission, a ray cast down for every pixel (<see cref="IEditorHost.MakeOverheadMap"/>), so it is
/// the level as it was loaded; Redraw makes it again. The marks are the document's, so they move as the mission is edited.
/// </remarks>
internal sealed class MapPanel : EditorElement
{
    private const int MapPixels = 1024;

    // What a click reaches, in pixels from a mark's middle.
    private const float PickRadius = 9f;

    // Task types that have a place but are the level's ground and light, too many to mark and nothing to move by hand.
    private static readonly HashSet<string> Unmarked = new(StringComparer.Ordinal)
    {
        "TerrainLightMap", "TextureModifier", "HeightMap", "LightmapInfo", "CubeModifier", "AIGraph", "Dirlight", "FlatSky",

        // Logic: placed where the original editor left them, which says nothing about the level.
        "StatusMessage", "EditVariable", "LevelTimer", "LevelFlow", "AnimTask", "CutScene", "DefineComputerObjective", "ConditionalSound",
    };

    // Shown only with Props on: a level has hundreds, and they are already on the map as their footprints.
    private static readonly HashSet<string> Props = new(StringComparer.Ordinal) { "EditRigidObj", "Building", "SplineObjWaypoint", "EditCamera" };

    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly RhiImageFactory _images;
    private readonly Button _redraw;
    private readonly Button _frame;
    private readonly Button _labels;
    private readonly Button _props;
    private readonly Button _guardsShown;
    private readonly GuardOrders _guards;
    private readonly PatrolPaths _paths;
    private OverheadMap? _map;
    private UiImage _image = UiImage.None;
    private int? _mapFor;
    private List<Mark>? _marks;
    private IReadOnlyList<Game.World.WorldPlacement>? _footprintsFor;

    // The view: the world point in the middle of the panel, in metres, and how many pixels a metre takes.
    private Vector3d _centre;
    private double _scale = 1.0;
    private bool _framed;

    private UiVec2? _panFrom;
    private Mark? _hover;
    private (UiVec2 From, Vector3d Offset, bool Moved)? _drag;

    /// <summary>Initializes a new instance of the <see cref="MapPanel"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="host">The editor host.</param>
    /// <param name="workspace">The open mission.</param>
    /// <param name="guards">The guards' orders, for how far and wide each sees.</param>
    public MapPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace, GuardOrders guards)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _guards = guards;
        _paths = new PatrolPaths(workspace);
        _guardsShown = new Button(theme, "Guards") { Style = ButtonStyle.Flat, IsToggle = true, IsChecked = false, Tooltip = "Draw every guard's sight and patrol and every camera's sweep, not only the selected ones'" };
        _guardsShown.Clicked += () => _guardsShown.IsChecked = !_guardsShown.IsChecked;
        AddChild(_guardsShown);
        _images = new RhiImageFactory(host.Device);
        _redraw = new Button(theme, "Redraw", KitIcon.History) { Style = ButtonStyle.Flat, Tooltip = "Draw the level from above again, as it is loaded now" };
        _redraw.Clicked += () => _mapFor = null;
        _frame = new Button(theme, "Frame All", KitIcon.Search) { Style = ButtonStyle.Flat, Tooltip = "Fit every object of the mission in view (Home)" };
        _frame.Clicked += FrameAll;
        _labels = new Button(theme, "Names") { Style = ButtonStyle.Flat, IsToggle = true, IsChecked = false, Tooltip = "Write every object's name beside its mark" };
        _labels.Clicked += () => _labels.IsChecked = !_labels.IsChecked;
        _props = new Button(theme, "Props") { Style = ButtonStyle.Flat, IsToggle = true, IsChecked = false, Tooltip = "Mark every prop, building, rail point and cutscene camera too" };
        _props.Clicked += () =>
        {
            _props.IsChecked = !_props.IsChecked;
            _marks = null;
        };
        foreach (Button button in (Button[])[_redraw, _frame, _labels, _props])
        {
            AddChild(button);
        }

        workspace.Changed += () => _marks = null;
    }

    /// <summary>Raised when a mark is double-clicked: the task to look at in the viewport.</summary>
    public event Action<MissionTask>? GoTo;

    /// <inheritdoc/>
    public override bool Focusable => true;

    private float Bar => Theme.ControlHeight + Theme.Px(10f);

    private UiRect MapRect => UiRect.FromEdges(Bounds.X, Bounds.Y + Bar, Bounds.Right, Bounds.Bottom);

    /// <summary>Draws every guard's sight and patrol, as the Guards button does.</summary>
    public void ShowAllGuards() => _guardsShown.IsChecked = true;

    /// <summary>Releases the picture.</summary>
    public void Release()
    {
        _images.DestroyImage(_image);
        _image = UiImage.None;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float x = contentRect.X + Theme.Px(6f);
        foreach (Button button in (Button[])[_redraw, _frame, _labels, _props, _guardsShown])
        {
            float width = Theme.Font.MeasureWidth(button.Text, Theme.TextSize) + Theme.Px(button.Icon == KitIcon.None ? 24f : 44f);
            button.Place(context, new UiRect(x, contentRect.Y + Theme.Px(5f), width, Theme.ControlHeight));
            x += width + Theme.Px(4f);
        }

        EnsureMap();
    }

    // The picture, made once the mission is up and again after Redraw or for another mission.
    private void EnsureMap()
    {
        int? mission = _workspace.Mission?.Id;
        if (mission == _mapFor || mission is null || _host.IsWarmingUp)
        {
            return;
        }

        _mapFor = mission;
        Release();
        _map = _host.MakeOverheadMap(MapPixels);
        if (_map is { } map)
        {
            _image = _images.CreateImage(map.Width, map.Height, map.Pixels);
            _host.Logger.Information("Editor", string.Create(CultureInfo.InvariantCulture, $"Drew the map: {map.Width}x{map.Height}, {map.MetresPerPixel:0.##} m a pixel."));
        }

        _framed = false;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        UiRect area = MapRect;
        painter.FillRect(area, Bgra32.FromRgb(18, 20, 22));
        if (_workspace.Document is null)
        {
            EditorDraw.Label(painter, Theme, "Open a mission to see it from above.", area with { X = area.X + Theme.Px(16f), Y = area.Y + Theme.Px(16f), Height = Theme.RowHeight }, Theme.TextDisabled);
            return;
        }

        if (!_framed && area.Width > 1f && (_map is not null || Marks().Count > 0))
        {
            _framed = true;
            FramePlay();
        }

        painter.PushClip(area);
        if (_map is { } map && _image.IsValid)
        {
            UiVec2 topLeft = ToScreen(new Vector3d(map.WestMetres, map.NorthMetres, 0));
            float size = (float)(map.MetresPerPixel * _scale);
            painter.DrawSprite(_image, new UiRect(topLeft.X, topLeft.Y, map.Width * size, map.Height * size), new UiRect(0f, 0f, 1f, 1f), Bgra32.White);
        }

        DrawFootprints(painter, area);
        Vector3d offset = _drag is { Moved: true } drag ? drag.Offset : default;
        HashSet<MissionTask> selected = [.. _workspace.Selection.Items];
        foreach (Mark mark in Marks())
        {
            bool isSelected = selected.Contains(mark.Task);
            Vector3d at = isSelected ? mark.At + offset : mark.At;
            DrawVolume(painter, mark, at, isSelected);
        }

        DrawGuards(painter, selected, offset);
        DrawCameras(painter, selected);
        DrawLinks(painter, selected);
        foreach (Mark mark in Marks())
        {
            bool isSelected = selected.Contains(mark.Task);
            UiVec2 p = ToScreen(isSelected ? mark.At + offset : mark.At);
            if (!area.Contains(p))
            {
                continue;
            }

            // People round, everything else a square: a level has a thousand marks, and a square is one quad.
            float r = Theme.Px(mark.Radius);
            if (isSelected)
            {
                painter.FillRect(new UiRect(p.X - r - Theme.Px(3f), p.Y - r - Theme.Px(3f), (r + Theme.Px(3f)) * 2f, (r + Theme.Px(3f)) * 2f), Theme.Accent);
            }

            if (mark.IsPerson)
            {
                painter.FillDot(p, r, mark.Colour);
            }
            else
            {
                painter.FillRect(new UiRect(p.X - r, p.Y - r, r * 2f, r * 2f), mark.Colour);
            }
            if (isSelected || ReferenceEquals(mark, _hover) || _labels.IsChecked)
            {
                string name = TaskPresentation.Name(mark.Task);
                EditorDraw.Label(painter, Theme, name, new UiRect(p.X + r + Theme.Px(4f), p.Y - (Theme.RowHeight * 0.5f), Theme.Font.MeasureWidth(name, Theme.TextSize) + Theme.Px(4f), Theme.RowHeight), isSelected ? Theme.Accent : Theme.Text);
            }
        }

        painter.PopClip();
        DrawScale(painter, area);
    }

    // The guards: which way each faces; for the selected ones (every one with Guards on) how far and wide it sees, from
    // its own script's orders over the game's default cone (0x44F060), and the patrol it walks along the AI graph.
    private void DrawGuards(UiPainter painter, HashSet<MissionTask> selected, Vector3d offset)
    {
        Bgra32 sight = Bgra32.FromRgb(0xF2, 0xD0, 0x4C);
        Bgra32 walk = Bgra32.FromRgb(0x6F, 0xB8, 0xFF);
        Bgra32 run = Bgra32.FromRgb(0xE8, 0x8A, 0x2E);
        Game.Ai.AiViewCone cone = Game.Ai.AiViewCone.DefaultPrimary;
        Span<UiVec2> fan = stackalloc UiVec2[14];
        foreach (Mark mark in Marks().Where(static m => m.IsPerson))
        {
            bool isSelected = selected.Contains(mark.Task);
            Vector3d at = isSelected ? mark.At + offset : mark.At;
            double heading = Heading(mark.Task);
            Vector3d forward = new(-Math.Sin(heading), Math.Cos(heading), 0);
            UiVec2 p = ToScreen(at);
            painter.StrokeLine(p, ToScreen(at + (forward * (Theme.Px(14f) / _scale))), Theme.Px(1.5f), mark.Colour);
            if (!isSelected && !_guardsShown.IsChecked || !PatrolPaths.IsSoldier(mark.Task))
            {
                continue;
            }

            Game.Ai.AiGuardOrders orders = _guards.Of(mark.Task);
            double length = orders.ViewLength ?? (cone.Length / Game.Ai.AiViewCone.UnitsPerMetre);
            double half = orders.ViewGamma is { } degrees ? degrees * Game.Ai.AiViewCone.DegreesToRadians : cone.Gamma;
            fan[0] = p;
            for (int i = 0; i < fan.Length - 1; i++)
            {
                double angle = heading - half + (2 * half * i / (fan.Length - 2));
                fan[i + 1] = ToScreen(at + (new Vector3d(-Math.Sin(angle), Math.Cos(angle), 0) * length));
            }

            painter.FillPolygon(fan, sight.WithAlpha((byte)(isSelected ? 60 : 30)));
            painter.StrokePolygon(fan, 1f, sight.WithAlpha((byte)(isSelected ? 200 : 90)));

            if (_paths.Walk(mark.Task) is { } route)
            {
                foreach (PatrolLeg leg in route.Legs)
                {
                    painter.StrokeLine(ToScreen(leg.From), ToScreen(leg.To), Theme.Px(leg.IsWayBack ? 1f : 2f), (leg.Run ? run : walk).WithAlpha((byte)(leg.IsWayBack ? 90 : isSelected ? 230 : 140)));
                }

                foreach (PatrolStop stop in route.Stops)
                {
                    UiVec2 s = ToScreen(stop.At);
                    painter.FillRect(new UiRect(s.X - Theme.Px(3f), s.Y - Theme.Px(3f), Theme.Px(6f), Theme.Px(6f)), stop.Stop.Run ? run : walk);
                }
            }
        }
    }

    // Security cameras: the arc the head sweeps, from Rotate Gamma Right to Left either side of where its holder and head
    // point (0x4420D0 builds the lens from yaw + yaw offset, the lens turning between -yaw_min and +yaw_max), each edge
    // widened by half the view cone, out to its range; and the cone where the head rests.
    private void DrawCameras(UiPainter painter, HashSet<MissionTask> selected)
    {
        Bgra32 colour = Bgra32.FromRgb(0xFF, 0x7A, 0x6E);
        Span<UiVec2> fan = stackalloc UiVec2[26];
        foreach (Mark mark in Marks().Where(static m => m.Task.TypeName == "SCamera"))
        {
            bool isSelected = selected.Contains(mark.Task);
            if (!isSelected && !_guardsShown.IsChecked)
            {
                continue;
            }

            MissionTask camera = mark.Task;
            double heading = Number(camera, "Holder Gamma") + Number(camera, "Camera Gamma");
            double right = Number(camera, "Rotate Gamma Right (d)", 45) * Math.PI / 180;
            double left = Number(camera, "Rotate Gamma Left (d)", 45) * Math.PI / 180;
            double half = Number(camera, "Viewcone Gamma (d)", 90) * Math.PI / 360;
            double range = Number(camera, "Viewcone length (m)", 10);
            Fan(fan, mark.At, heading - right - half, heading + left + half, range);
            painter.FillPolygon(fan, colour.WithAlpha((byte)(isSelected ? 40 : 22)));
            painter.StrokePolygon(fan, 1f, colour.WithAlpha((byte)(isSelected ? 200 : 100)));
            Fan(fan[..12], mark.At, heading - half, heading + half, range);
            painter.FillPolygon(fan[..12], colour.WithAlpha((byte)(isSelected ? 70 : 35)));
        }
    }

    // A fan from a point between two headings, out to a distance, into the points given.
    private void Fan(Span<UiVec2> points, Vector3d at, double from, double to, double metres)
    {
        points[0] = ToScreen(at);
        for (int i = 1; i < points.Length; i++)
        {
            double angle = from + ((to - from) * (i - 1) / (points.Length - 2));
            points[i] = ToScreen(at + (new Vector3d(-Math.Sin(angle), Math.Cos(angle), 0) * metres));
        }
    }

    private static double Number(MissionTask task, string parameter, double fallback = 0) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) && values.Length > 0 ? values[0] : fallback;

    // What the selected objects are wired to in the mission's logic: lines in from every object their conditions read,
    // and out to every object whose conditions read them, as the mission graph wires them; and a guard's alarm.
    private void DrawLinks(UiPainter painter, HashSet<MissionTask> selected)
    {
        if (selected.Count == 0 || selected.Count > 8 || _workspace.Logic.Graph is not { } graph)
        {
            return;
        }

        Bgra32 reads = Bgra32.FromRgb(0x5C, 0xD6, 0xE8);
        Bgra32 readBy = Bgra32.FromRgb(0xF0, 0x9A, 0x3C);
        foreach (MissionTask task in selected)
        {
            if (!graph.TryGetNode(task, out Core.Graphs.TaskGraphNode? node) || TaskPresentation.PositionMetres(task) is not { } here)
            {
                continue;
            }

            HashSet<MissionTask> sources = new(ReferenceEqualityComparer.Instance);
            HashSet<MissionTask> readers = new(ReferenceEqualityComparer.Instance);
            foreach (Core.Graphs.GraphLink link in graph.Links)
            {
                if (link.From.Node == node.Key && graph.Nodes.GetValueOrDefault(link.To.Node) is { } to && OwnerOf(graph, to) is { } reader && !ReferenceEquals(reader, task))
                {
                    readers.Add(reader);
                }
                else if (graph.Nodes.GetValueOrDefault(link.To.Node) is { } into && ReferenceEquals(OwnerOf(graph, into), task)
                    && graph.Nodes.GetValueOrDefault(link.From.Node) is Core.Graphs.TaskGraphNode source && !ReferenceEquals(source.Task, task))
                {
                    sources.Add(source.Task);
                }
            }

            // An alarm panel's guards: every soldier whose own script sends it to this alarm.
            if (task.TypeName == "AlarmControl" && task.Id >= 0)
            {
                foreach (Mark guard in Marks().Where(m => PatrolPaths.IsSoldier(m.Task) && _guards.Of(m.Task).AlarmControlId == task.Id))
                {
                    readers.Add(guard.Task);
                }
            }

            if (PatrolPaths.IsSoldier(task) && _guards.Of(task).AlarmControlId is { } alarm
                && _workspace.Document?.EnumerateTasks().FirstOrDefault(t => t.Id == alarm && t.TypeName == "AlarmControl") is { } control)
            {
                readers.Add(control);
            }

            foreach ((HashSet<MissionTask> set, Bgra32 colour, bool inward) in ((HashSet<MissionTask>, Bgra32, bool)[])[(sources, reads, true), (readers, readBy, false)])
            {
                foreach (MissionTask other in set)
                {
                    if (TaskPresentation.PositionMetres(other) is not { } there)
                    {
                        continue;
                    }

                    UiVec2 from = ToScreen(inward ? there : here), to = ToScreen(inward ? here : there);
                    painter.StrokeLine(from, to, Theme.Px(1.5f), colour.WithAlpha(210));
                    UiVec2 along = to - from;
                    float length = MathF.Max(1f, MathF.Sqrt((along.X * along.X) + (along.Y * along.Y)));
                    UiVec2 unit = new(along.X / length, along.Y / length), side = new(-unit.Y, unit.X);
                    UiVec2 tip = to - (unit * Theme.Px(8f));
                    painter.FillTriangle(tip + (unit * Theme.Px(6f)), tip + (side * Theme.Px(4f)), tip - (side * Theme.Px(4f)), colour);
                    string name = TaskPresentation.Name(other);
                    UiVec2 label = ToScreen(there);
                    EditorDraw.Label(painter, Theme, name, new UiRect(label.X + Theme.Px(8f), label.Y + Theme.Px(2f), Theme.Font.MeasureWidth(name, Theme.TextSize) + Theme.Px(4f), Theme.RowHeight), colour);
                }
            }
        }
    }

    // The task an expression node belongs to, or the task a task node is.
    private static MissionTask? OwnerOf(Core.Graphs.MissionGraph graph, Core.Graphs.GraphNode node) => node switch
    {
        Core.Graphs.TaskGraphNode task => task.Task,
        Core.Graphs.ExpressionGraphNode expression when graph.Nodes.GetValueOrDefault(expression.Owner.Task) is Core.Graphs.TaskGraphNode owner => owner.Task,
        _ => null,
    };

    // Which way a person faces, in radians about the up axis: a soldier's Gamma (0x45F890 turns its body by it alone), or the
    // turn of a task's orientation; forward is the body's +Y.
    private static double Heading(MissionTask task)
    {
        if (task.FindParameter("Gamma") is { } gamma && gamma.TryGetNumbers(out double[] value) && value.Length == 1)
        {
            return value[0];
        }

        Vector3d forward = TaskPresentation.OrientationMatrix(task).Transposed().Transform(new Vector3d(0, 1, 0));
        return Math.Atan2(-forward.X, forward.Y);
    }

    // What the level placed, each model's box seen from above in stone grey: the buildings and props the ground picture,
    // cast on the terrain alone, does not show. Worked out in metres once a level; put on screen each frame.
    private void DrawFootprints(UiPainter painter, UiRect area)
    {
        IReadOnlyList<Game.World.WorldPlacement> placements = _host.Placements;
        if (!ReferenceEquals(placements, _footprintsFor))
        {
            _footprintsFor = placements;
            _worldFootprints = [.. placements.Where(static p => !p.IsSuppressed && p.Model.Vertices.Length > 0).Select(Footprint)];
        }

        Bgra32 fill = Bgra32.FromRgb(0xB8, 0xB2, 0xA4).WithAlpha(150);
        Bgra32 edge = Bgra32.FromRgb(0x24, 0x22, 0x20).WithAlpha(190);
        Span<UiVec2> outline = stackalloc UiVec2[8];
        foreach (Vector3d[] hull in _worldFootprints)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < hull.Length; i++)
            {
                outline[i] = ToScreen(hull[i]);
                minX = MathF.Min(minX, outline[i].X);
                maxX = MathF.Max(maxX, outline[i].X);
                minY = MathF.Min(minY, outline[i].Y);
                maxY = MathF.Max(maxY, outline[i].Y);
            }

            if (maxX < area.X || minX > area.Right || maxY < area.Y || minY > area.Bottom || (maxX - minX) + (maxY - minY) < 2f)
            {
                continue;
            }

            painter.FillPolygon(outline[..hull.Length], fill);
            if ((maxX - minX) + (maxY - minY) > 12f)
            {
                painter.StrokePolygon(outline[..hull.Length], 1f, edge);
            }
        }
    }

    private List<Vector3d[]> _worldFootprints = [];

    // A placement's outline from above: its model's box, all eight corners taken into the world as it is placed (model to
    // world is the orientation's transpose, in the model's units), and the hull of where they fall on the ground.
    private static Vector3d[] Footprint(Game.World.WorldPlacement placement)
    {
        Vector3f min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f max = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (Formats.World.MefVertex vertex in placement.Model.Vertices)
        {
            min = new Vector3f(MathF.Min(min.X, vertex.Position.X), MathF.Min(min.Y, vertex.Position.Y), MathF.Min(min.Z, vertex.Position.Z));
            max = new Vector3f(MathF.Max(max.X, vertex.Position.X), MathF.Max(max.Y, vertex.Position.Y), MathF.Max(max.Z, vertex.Position.Z));
        }

        Matrix3f toWorld = placement.Orientation.Transposed();
        List<Vector3d> corners = [];
        foreach (float x in (ReadOnlySpan<float>)[min.X, max.X])
        {
            foreach (float y in (ReadOnlySpan<float>)[min.Y, max.Y])
            {
                foreach (float z in (ReadOnlySpan<float>)[min.Z, max.Z])
                {
                    corners.Add(placement.Position + (toWorld.Transform(new Vector3d(x, y, z)) / WorldUnits.PerMetre));
                }
            }
        }

        return Hull(corners);
    }

    // The convex hull of points seen from above, anticlockwise (Andrew's monotone chain).
    private static Vector3d[] Hull(List<Vector3d> points)
    {
        points.Sort(static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        List<Vector3d> hull = [];
        static double Cross(Vector3d o, Vector3d a, Vector3d b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
        foreach (int pass in (ReadOnlySpan<int>)[0, 1])
        {
            int start = hull.Count;
            for (int k = 0; k < points.Count; k++)
            {
                Vector3d p = points[pass == 0 ? k : points.Count - 1 - k];
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return [.. hull];
    }

    // An area's box, turned as the game turns it, where the task is one.
    private void DrawVolume(UiPainter painter, Mark mark, Vector3d at, bool isSelected)
    {
        if (TaskVolume.Of(mark.Task) is not { } volume || volume.HalfMetres() is not { } half)
        {
            return;
        }

        Matrix3f orientation = TaskPresentation.OrientationMatrix(mark.Task);
        Span<UiVec2> corners =
        [
            ToScreen(TaskVolume.ToWorld(at, orientation, new Vector3d(-half.X, -half.Y, 0))),
            ToScreen(TaskVolume.ToWorld(at, orientation, new Vector3d(half.X, -half.Y, 0))),
            ToScreen(TaskVolume.ToWorld(at, orientation, new Vector3d(half.X, half.Y, 0))),
            ToScreen(TaskVolume.ToWorld(at, orientation, new Vector3d(-half.X, half.Y, 0))),
        ];
        Bgra32 colour = isSelected ? Theme.Accent : mark.Colour;
        if (isSelected || mark.Task.TypeName != "AmbientArea")
        {
            painter.FillPolygon(corners, colour.WithAlpha((byte)(isSelected ? 50 : 28)));
        }

        painter.StrokePolygon(corners, Theme.Px(1.5f), colour.WithAlpha(200));
    }

    // A bar of a round number of metres in the corner.
    private void DrawScale(UiPainter painter, UiRect area)
    {
        double metres = Math.Pow(10, Math.Floor(Math.Log10(Theme.Px(120f) / _scale)));
        float length = (float)(metres * _scale);
        UiVec2 from = new(area.X + Theme.Px(14f), area.Bottom - Theme.Px(16f));
        painter.StrokeLine(from, from + new UiVec2(length, 0f), Theme.Px(2f), Theme.Text);
        string text = metres >= 1000 ? string.Create(CultureInfo.InvariantCulture, $"{metres / 1000:0.#} km") : string.Create(CultureInfo.InvariantCulture, $"{metres:0} m");
        EditorDraw.Label(painter, Theme, text, new UiRect(from.X, from.Y - Theme.RowHeight - Theme.Px(2f), Theme.Px(80f), Theme.RowHeight), Theme.Text);
    }

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        // About the point under the pointer, which stays under it.
        Vector3d before = ToWorld(position);
        _scale = Math.Clamp(_scale * Math.Pow(1.2, notches), 0.02, 40.0);
        Vector3d after = ToWorld(position);
        _centre += before - after;
        return true;
    }

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        if (!MapRect.Contains(e.Position))
        {
            return false;
        }

        if (e.Button is MouseButton.Right or MouseButton.Middle)
        {
            _panFrom = e.Position;
            return true;
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        Mark? hit = Pick(e.Position);
        if (e.ClickCount >= 2 && hit is not null)
        {
            GoTo?.Invoke(hit.Task);
            return true;
        }

        if (hit is null)
        {
            if (!e.Shift)
            {
                _workspace.Selection.Clear();
            }

            return true;
        }

        if (e.Shift)
        {
            _workspace.Selection.Toggle(hit.Task);
        }
        else if (!_workspace.Selection.Contains(hit.Task))
        {
            _workspace.Selection.Set(hit.Task);
        }

        _drag = (e.Position, default, false);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMove(in PointerEvent e)
    {
        if (_panFrom is { } from)
        {
            UiVec2 d = e.Position - from;
            _centre += new Vector3d(-d.X / _scale, d.Y / _scale, 0);
            _panFrom = e.Position;
            return;
        }

        if (_drag is { } drag)
        {
            UiVec2 d = e.Position - drag.From;
            bool moved = drag.Moved || MathF.Abs(d.X) + MathF.Abs(d.Y) > Theme.Px(3f);
            _drag = (drag.From, new Vector3d(d.X / _scale, -d.Y / _scale, 0), moved);
            return;
        }

        _hover = MapRect.Contains(e.Position) ? Pick(e.Position) : null;
    }

    /// <inheritdoc/>
    protected override void OnPointerUp(in PointerEvent e)
    {
        _panFrom = null;
        if (_drag is { Moved: true } drag)
        {
            Move(drag.Offset);
        }

        _drag = null;
    }

    /// <inheritdoc/>
    protected override void OnCaptureLost()
    {
        _panFrom = null;
        _drag = null;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(in KeyEvent e)
    {
        if (e.Key is KeyCode.Home or KeyCode.F)
        {
            if (e.Key == KeyCode.F && _workspace.Selection.Primary is { } task && Marks().FirstOrDefault(m => m.Task == task) is { } mark)
            {
                _centre = mark.At;
            }
            else
            {
                FrameAll();
            }

            return true;
        }

        return false;
    }

    // The selected objects moved over the ground, as one step: each keeps its height over whatever it stood on.
    private void Move(Vector3d offset)
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        List<IEditorCommand> steps = [];
        foreach (MissionTask task in _workspace.Selection.Items)
        {
            if (TaskPresentation.Position(task) is not { } position || TaskPresentation.PositionMetres(task) is not { } at)
            {
                continue;
            }

            Vector3d to = at + offset;
            if (_map?.HeightAt(at.X, at.Y) is { } before && _map.HeightAt(to.X, to.Y) is { } after)
            {
                to = new Vector3d(to.X, to.Y, at.Z + (after - before));
            }

            steps.Add(SetParameterCommand.Numbers(document, task, position.Name, [WorldUnits.FromMetres(to.X), WorldUnits.FromMetres(to.Y), WorldUnits.FromMetres(to.Z)]));
        }

        if (steps.Count > 0)
        {
            _workspace.Execute(new CompositeCommand(steps.Count == 1 ? "Move on Map" : string.Create(CultureInfo.InvariantCulture, $"Move {steps.Count} on Map"), steps));
        }
    }

    // Where the mission is played: the box round its people, the odd far-flung one left out, with room round it; the whole
    // picture when it has none.
    private void FramePlay()
    {
        Mark[] people = [.. Marks().Where(static m => m.IsPerson)];
        UiRect area = MapRect;
        if (people.Length < 2 || area.Width <= 1f)
        {
            FrameAll();
            return;
        }

        double[] xs = [.. people.Select(static m => m.At.X).Order()];
        double[] ys = [.. people.Select(static m => m.At.Y).Order()];
        int cut = people.Length / 10;
        double minX = xs[cut], maxX = xs[xs.Length - 1 - cut];
        double minY = ys[cut], maxY = ys[ys.Length - 1 - cut];
        _centre = new Vector3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0);
        _scale = Math.Clamp(Math.Min(area.Width / Math.Max(60.0, (maxX - minX) * 1.3), area.Height / Math.Max(60.0, (maxY - minY) * 1.3)), 0.02, 40.0);
    }

    private void FrameAll()
    {
        IReadOnlyList<Mark> marks = Marks();
        UiRect area = MapRect;
        if (area.Width <= 1f)
        {
            return;
        }

        // The picture is already the level's extent, its far-flung objects left out: framed on that when there is one.
        if (_map is { } map)
        {
            double wide = map.Width * map.MetresPerPixel;
            double high = map.Height * map.MetresPerPixel;
            _centre = new Vector3d(map.WestMetres + (wide * 0.5), map.NorthMetres - (high * 0.5), 0);
            _scale = Math.Clamp(Math.Min(area.Width / wide, area.Height / high), 0.02, 40.0);
            return;
        }

        if (marks.Count == 0)
        {
            return;
        }

        // The middle nine tenths of the marks: a mission's odd far-flung marker does not shrink the rest to nothing.
        double[] xs = [.. marks.Select(static m => m.At.X).Order()];
        double[] ys = [.. marks.Select(static m => m.At.Y).Order()];
        double minX = xs[xs.Length / 20], maxX = xs[xs.Length - 1 - (xs.Length / 20)];
        double minY = ys[ys.Length / 20], maxY = ys[ys.Length - 1 - (ys.Length / 20)];
        _centre = new Vector3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0);
        _scale = Math.Clamp(Math.Min(area.Width / Math.Max(20.0, (maxX - minX) * 1.15), area.Height / Math.Max(20.0, (maxY - minY) * 1.15)), 0.02, 40.0);
    }

    private Mark? Pick(UiVec2 at)
    {
        Mark? best = null;
        float bestDistance = Theme.Px(PickRadius);
        foreach (Mark mark in Marks())
        {
            float distance = UiVec2.Distance(ToScreen(mark.At), at);
            if (distance < bestDistance || (distance <= bestDistance && best is not null && mark.Radius > best.Radius))
            {
                best = mark;
                bestDistance = distance;
            }
        }

        return best;
    }

    private UiVec2 ToScreen(Vector3d world)
    {
        UiRect area = MapRect;
        return new UiVec2(
            (float)(area.Center.X + ((world.X - _centre.X) * _scale)),
            (float)(area.Center.Y - ((world.Y - _centre.Y) * _scale)));
    }

    private Vector3d ToWorld(UiVec2 screen)
    {
        UiRect area = MapRect;
        return new Vector3d(_centre.X + ((screen.X - area.Center.X) / _scale), _centre.Y - ((screen.Y - area.Center.Y) / _scale), 0);
    }

    // Every object of the mission with a place, from the document as it stands.
    private List<Mark> Marks()
    {
        if (_marks is not null)
        {
            return _marks;
        }

        _marks = [];
        if (_workspace.Document is not { } document)
        {
            return _marks;
        }

        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (Unmarked.Contains(task.TypeName) || (!_props.IsChecked && Props.Contains(task.TypeName)) || TaskPresentation.PositionMetres(task) is not { } at)
            {
                continue;
            }

            (Bgra32 colour, float radius) = task.TypeName switch
            {
                "HumanPlayer" => (Bgra32.FromRgb(0x4F, 0xA8, 0xFF), 6.5f),
                _ when task.TypeName.StartsWith("HumanSoldier", StringComparison.Ordinal) => (Bgra32.FromRgb(0xE5, 0x48, 0x3E), 5f),
                _ when task.TypeName.EndsWith("Pickup", StringComparison.Ordinal) => (Bgra32.FromRgb(0xF2, 0xC9, 0x4C), 4f),
                "AreaActivate" or "AmbientArea" => (Bgra32.FromRgb(0x5C, 0xC8, 0x8A), 3.5f),
                "Building" => (Bgra32.FromRgb(0x9A, 0x9A, 0x9A), 3f),
                _ => (EditorIcons.KindColour(TaskPresentation.Icon(task)) ?? Bgra32.FromRgb(0xC8, 0xC8, 0xC8), 3.5f),
            };
            _marks.Add(new Mark(task, at, colour.WithAlpha(220), radius, task.TypeName.StartsWith("Human", StringComparison.Ordinal)));
        }

        return _marks;
    }

    // One object's mark.
    private sealed record Mark(MissionTask Task, Vector3d At, Bgra32 Colour, float Radius, bool IsPerson);
}
