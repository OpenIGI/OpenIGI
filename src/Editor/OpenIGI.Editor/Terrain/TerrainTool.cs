using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Viewport;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Terrain;

/// <summary>
/// The viewport's terrain tool: a ring on the ground under the pointer showing the brush, and while the button is held, the
/// brush shaping the ground under it every frame as one undoable stroke.
/// </summary>
/// <param name="host">The running game.</param>
/// <param name="workspace">The open mission, whose history records each stroke.</param>
/// <param name="brush">The brush.</param>
/// <param name="live">The drawn world, for the footprints of the objects standing on the ground.</param>
/// <param name="notify">Tells the user something about a stroke, such as the ground reaching the furthest it can go.</param>
internal sealed class TerrainTool(IEditorHost host, MissionWorkspace workspace, TerrainBrush brush, LiveWorldLink live, Action<string>? notify = null)
{
    private const double ReachMetres = 3000.0;
    private const int RingSegments = 48;

    private Vector3d? _at;
    private TerrainStroke? _stroke;
    private TerrainPaintStroke? _paint;
    private IEditableTerrain? _painting;
    private double _flattenTo;
    private GroundedObjects? _grounded;
    private bool _inverted;

    /// <summary>Gets the brush the tool paints with.</summary>
    public TerrainBrush Brush => brush;

    /// <summary>Gets whether the open mission's ground can be shaped.</summary>
    public bool IsAvailable => host.Terrain is not null;

    /// <summary>Gets whether a stroke is being painted.</summary>
    public bool IsPainting => _stroke is not null || _paint is not null;

    /// <summary>Follows the pointer: finds the ground under it.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="point">The pointer.</param>
    public void Hover(in ViewportProjector projector, UiVec2 point)
    {
        _at = host.Terrain is { } terrain && projector.TryRay(point, out Vector3d direction)
            && terrain.TryRaycast(projector.Eye, direction, ReachMetres, out Vector3d hit)
                ? hit
                : null;
    }

    /// <summary>Starts a stroke where the pointer meets the ground.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="point">The press.</param>
    /// <param name="inverted">Whether to paint the opposite of the brush's mode (Shift).</param>
    /// <returns>Whether a stroke started.</returns>
    public bool Begin(in ViewportProjector projector, UiVec2 point, bool inverted)
    {
        Hover(projector, point);
        if (host.Terrain is not { } terrain || _at is not { } at)
        {
            return false;
        }

        _painting = terrain;
        _inverted = inverted;
        if (brush.Mode == TerrainBrushMode.Paint)
        {
            if ((uint)brush.PaintLayer >= (uint)terrain.PaintLayers.Count)
            {
                notify?.Invoke(terrain.PaintLayers.Count == 0 ? "This mission's ground has no materials to paint." : "Pick a material to paint in the Terrain panel.");
                _painting = null;
                return false;
            }

            _paint = new TerrainPaintStroke(terrain, brush.PaintLayer);
            return true;
        }

        _stroke = new TerrainStroke(terrain);
        _grounded = brush.Objects != TerrainObjectMode.Ignore && workspace.Document is { } document
            ? GroundedObjects.Capture(document, live, terrain)
            : null;
        _flattenTo = at.Z;
        _inverted = inverted;
        return true;
    }

    /// <summary>Paints for a frame while a stroke is held. Called once per frame.</summary>
    /// <param name="elapsedSeconds">The frame time.</param>
    public void Tick(float elapsedSeconds)
    {
        if (_paint is { } paint && _at is { } spot)
        {
            // Shift takes the material away.
            paint.Dab(spot.X, spot.Y, brush.RadiusMetres, !_inverted);
            return;
        }

        if (_stroke is { } stroke && _painting is { } terrain && _at is { } at)
        {
            // A long frame is not allowed to dig a pit: the brush works for at most a tenth of a second at once.
            Func<double, double, bool>? keepOff = brush.Objects == TerrainObjectMode.Protect && _grounded is { } grounded ? grounded.Protects : null;
            brush.Apply(terrain, at, Math.Min(elapsedSeconds, 0.1), _flattenTo, stroke, _inverted ? TerrainBrush.Opposite(brush.Mode) : null, keepOff);
        }
    }

    /// <summary>Ends the stroke and records it as one step.</summary>
    public void End()
    {
        if (_paint is { } paint)
        {
            if (!paint.IsEmpty)
            {
                workspace.History.BreakMerge();
                workspace.History.Record(paint);
            }

            string where = _at is { } spot ? FormattableString.Invariant($" at {spot.X:0}, {spot.Y:0}") : string.Empty;
            host.Logger.Information("Editor", paint.IsEmpty
                ? $"The paint stroke{where} changed nothing: the ground there already had that material, or lies outside its square."
                : $"Painted {paint.Count} cell(s){where}.");

            _paint = null;
            _painting = null;
            return;
        }

        if (_stroke is not { } stroke)
        {
            return;
        }

        // Whatever the stroke's last frames put off (a mountain area's rebuild) is done before it is recorded.
        _painting?.Settle();
        stroke.Finish();
        if (!stroke.IsEmpty)
        {
            // Objects standing on the shaped ground follow it, in the same step as the stroke.
            IReadOnlyList<IEditorCommand> follow = brush.Objects == TerrainObjectMode.MoveWithGround && _grounded is { } grounded
                && workspace.Document is { } document && _painting is { } shaped
                    ? grounded.Follow(document, live, shaped)
                    : [];
            foreach (IEditorCommand move in follow)
            {
                move.Apply();
            }

            workspace.History.BreakMerge();
            workspace.History.Record(follow.Count == 0 ? stroke : new CompositeCommand(stroke.Name, [stroke, .. follow]));
            if (follow.Count > 0)
            {
                host.Logger.Information("Editor", $"{follow.Count} object(s) moved with the ground.");
            }
        }

        if (_painting is { } terrain && terrain.TakeClampCount() > 0 && terrain.LimitDescription is { } limit)
        {
            notify?.Invoke(limit);
        }

        _stroke = null;
        _painting = null;
        _grounded = null;
    }

    /// <summary>Abandons the stroke, putting the ground back as it was before it.</summary>
    public void Cancel()
    {
        _stroke?.Undo();
        _paint?.Undo();
        _stroke = null;
        _paint = null;
        _painting = null;
    }

    /// <summary>Grows or shrinks the brush by a step.</summary>
    /// <param name="larger">Whether to grow it.</param>
    public void Resize(bool larger) =>
        brush.RadiusMetres = Math.Clamp(brush.RadiusMetres * (larger ? 1.25 : 0.8), 0.5, 500.0);

    /// <summary>Draws the brush's ring on the ground.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="projector">The view.</param>
    public void Draw(UiPainter painter, EditorTheme theme, in ViewportProjector projector)
    {
        if (host.Terrain is not { } terrain)
        {
            return;
        }

        if (brush.Mode == TerrainBrushMode.Paint && (uint)brush.PaintLayer < (uint)terrain.PaintLayers.Count)
        {
            // The square the material can be painted in.
            DrawSquare(painter, theme, projector, terrain, terrain.PaintLayers[brush.PaintLayer]);
        }
        else if (terrain.IsPartial)
        {
            foreach (ITerrainHeightGrid grid in terrain.Grids)
            {
                DrawOutline(painter, theme, projector, grid);
            }
        }

        if (_at is not { } at)
        {
            return;
        }

        Bgra32 colour = IsPainting ? Bgra32.FromRgb(255, 222, 90) : theme.Accent;
        UiVec2? previous = null;
        for (int i = 0; i <= RingSegments; i++)
        {
            double angle = i * (Math.PI * 2.0 / RingSegments);
            double x = at.X + (Math.Cos(angle) * brush.RadiusMetres);
            double y = at.Y + (Math.Sin(angle) * brush.RadiusMetres);
            double z = terrain.TryGetHeight(x, y, out double ground) ? ground : at.Z;
            UiVec2? screen = projector.TryProject(new Vector3d(x, y, z + 0.05), out UiVec2 s) ? s : null;
            if (previous is { } a && screen is { } b)
            {
                painter.StrokeLine(a, b, theme.Px(1.5f), colour);
            }

            previous = screen;
        }

        if (projector.TryProject(at, out UiVec2 middle))
        {
            painter.FillDot(middle, theme.Px(3f), colour);
        }
    }

    // A paint layer's square laid along the ground.
    private static void DrawSquare(UiPainter painter, EditorTheme theme, in ViewportProjector projector, IEditableTerrain terrain, TerrainPaintLayer layer)
    {
        Bgra32 colour = theme.Accent.WithAlpha(150);
        (double X, double Y)[] corners = [(layer.MinX, layer.MinY), (layer.MaxX, layer.MinY), (layer.MaxX, layer.MaxY), (layer.MinX, layer.MaxY)];
        const int Steps = 32;
        for (int side = 0; side < 4; side++)
        {
            (double x0, double y0) = corners[side];
            (double x1, double y1) = corners[(side + 1) % 4];
            UiVec2? previous = null;
            for (int i = 0; i <= Steps; i++)
            {
                double x = x0 + ((x1 - x0) * i / Steps);
                double y = y0 + ((y1 - y0) * i / Steps);
                UiVec2? screen = terrain.TryGetHeight(x, y, out double z) && projector.TryProject(new Vector3d(x, y, z + 0.1), out UiVec2 s) ? s : null;
                if (previous is { } a && screen is { } b)
                {
                    painter.StrokeLine(a, b, theme.Px(1f), colour);
                }

                previous = screen;
            }
        }
    }

    // A grid's edge laid along the ground, so the parts that can be shaped show where they end.
    private static void DrawOutline(UiPainter painter, EditorTheme theme, in ViewportProjector projector, ITerrainHeightGrid grid)
    {
        Bgra32 colour = theme.Accent.WithAlpha(150);
        int step = Math.Max(1, (grid.Width - 1) / 32);
        (int X, int Y)[] corners = [(0, 0), (grid.Width - 1, 0), (grid.Width - 1, grid.Height - 1), (0, grid.Height - 1)];
        for (int side = 0; side < 4; side++)
        {
            (int x0, int y0) = corners[side];
            (int x1, int y1) = corners[(side + 1) % 4];
            int count = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) / step;
            UiVec2? previous = null;
            for (int i = 0; i <= count; i++)
            {
                int x = x0 + ((x1 - x0) * i / Math.Max(1, count));
                int y = y0 + ((y1 - y0) * i / Math.Max(1, count));
                (double mx, double my) = grid.SampleMetres(x, y);
                UiVec2? screen = projector.TryProject(new Vector3d(mx, my, grid.GetHeight(x, y) + 0.1), out UiVec2 s) ? s : null;
                if (previous is { } a && screen is { } b)
                {
                    painter.StrokeLine(a, b, theme.Px(1f), colour);
                }

                previous = screen;
            }
        }
    }
}
