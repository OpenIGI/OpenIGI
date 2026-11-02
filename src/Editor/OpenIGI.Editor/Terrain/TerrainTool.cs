using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
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
internal sealed class TerrainTool(IEditorHost host, MissionWorkspace workspace, TerrainBrush brush)
{
    private const double ReachMetres = 3000.0;
    private const int RingSegments = 48;

    private Vector3d? _at;
    private TerrainStroke? _stroke;
    private IEditableTerrain? _painting;
    private double _flattenTo;
    private bool _inverted;

    /// <summary>Gets whether the open mission's ground can be shaped.</summary>
    public bool IsAvailable => host.Terrain is not null;

    /// <summary>Gets whether a stroke is being painted.</summary>
    public bool IsPainting => _stroke is not null;

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
        _stroke = new TerrainStroke(terrain);
        _flattenTo = at.Z;
        _inverted = inverted;
        return true;
    }

    /// <summary>Paints for a frame while a stroke is held. Called once per frame.</summary>
    /// <param name="elapsedSeconds">The frame time.</param>
    public void Tick(float elapsedSeconds)
    {
        if (_stroke is { } stroke && _painting is { } terrain && _at is { } at)
        {
            // A long frame is not allowed to dig a pit: the brush works for at most a tenth of a second at once.
            brush.Apply(terrain, at, Math.Min(elapsedSeconds, 0.1), _flattenTo, stroke, _inverted ? TerrainBrush.Opposite(brush.Mode) : null);
        }
    }

    /// <summary>Ends the stroke and records it as one step.</summary>
    public void End()
    {
        if (_stroke is not { } stroke)
        {
            return;
        }

        stroke.Finish();
        if (!stroke.IsEmpty)
        {
            workspace.History.BreakMerge();
            workspace.History.Record(stroke);
        }

        _stroke = null;
        _painting = null;
    }

    /// <summary>Abandons the stroke, putting the ground back as it was before it.</summary>
    public void Cancel()
    {
        _stroke?.Undo();
        _stroke = null;
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
        if (_at is not { } at || host.Terrain is not { } terrain)
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
}
