using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The resize gizmo for a volume: its box drawn in the world with a handle on each face. Dragging a face moves that face
/// alone, the opposite face staying put, so the centre follows by half the drag.
/// </summary>
internal sealed class BoxGizmo
{
    private const float GrabPixels = 8f;

    private int _face = -1;
    private Vector3d _startCentre;
    private Vector3d _startHalf;
    private Vector3d _direction;
    private UiVec2 _pointerStart;
    private UiVec2 _axisScreen;
    private double _pixelsPerMetre;

    /// <summary>Gets whether a face is being dragged.</summary>
    public bool IsDragging => _face >= 0;

    /// <summary>Gets the face under the pointer, 0–5 (+X, −X, +Y, −Y, +Z, −Z), or -1.</summary>
    public int Hot { get; private set; } = -1;

    /// <summary>Gets or sets the snap step for sizes, in metres; zero for none.</summary>
    public double Snap { get; set; }

    /// <summary>Updates the hover highlight.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="box">The box.</param>
    /// <param name="point">The pointer.</param>
    public void Hover(in ViewportProjector projector, in Box box, UiVec2 point)
    {
        if (!IsDragging)
        {
            Hot = FaceAt(projector, box, point);
        }
    }

    /// <summary>Starts a drag if the press is on a face handle.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="box">The box.</param>
    /// <param name="point">The press.</param>
    /// <returns>Whether a drag started.</returns>
    public bool Begin(in ViewportProjector projector, in Box box, UiVec2 point)
    {
        int face = FaceAt(projector, box, point);
        if (face < 0)
        {
            return false;
        }

        // The face's outward direction measured on screen over a metre, so pointer travel converts to metres along it.
        Vector3d handle = box.FaceCentre(face);
        Vector3d direction = box.FaceDirection(face);
        if (!projector.TryProject(handle, out UiVec2 a) || !projector.TryProject(handle + direction, out UiVec2 b))
        {
            return false;
        }

        UiVec2 along = new(b.X - a.X, b.Y - a.Y);
        float length = along.Length;
        if (length < 0.5f)
        {
            // Pointing straight at the eye: there is no telling which way is out.
            return false;
        }

        _face = face;
        Hot = face;
        _direction = direction;
        _axisScreen = new UiVec2(along.X / length, along.Y / length);
        _pixelsPerMetre = length;
        _pointerStart = point;
        _startCentre = box.Centre;
        _startHalf = box.Half;
        return true;
    }

    /// <summary>Follows the pointer.</summary>
    /// <param name="point">The pointer.</param>
    /// <param name="fine">Whether to move at a tenth of the speed.</param>
    /// <returns>The box's new centre and half sizes, in metres.</returns>
    public (Vector3d Centre, Vector3d Half) Drag(UiVec2 point, bool fine)
    {
        double pixels = ((point.X - _pointerStart.X) * _axisScreen.X) + ((point.Y - _pointerStart.Y) * _axisScreen.Y);
        double metres = pixels / _pixelsPerMetre * (fine ? 0.1 : 1.0);
        int axis = _face / 2;
        double startSize = Component(_startHalf, axis) * 2.0;
        double size = Math.Max(0.01, startSize + metres);
        if (Snap > 0)
        {
            size = Math.Max(Snap, Math.Round(size / Snap) * Snap);
        }

        Vector3d half = axis switch
        {
            0 => new Vector3d(size * 0.5, _startHalf.Y, _startHalf.Z),
            1 => new Vector3d(_startHalf.X, size * 0.5, _startHalf.Z),
            _ => new Vector3d(_startHalf.X, _startHalf.Y, size * 0.5),
        };
        return (_startCentre + (_direction * ((size - startSize) * 0.5)), half);
    }

    /// <summary>Ends a drag.</summary>
    public void End() => _face = -1;

    /// <summary>Draws the box and its handles.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="projector">The view.</param>
    /// <param name="box">The box.</param>
    /// <param name="handles">Whether to draw the face handles.</param>
    /// <param name="colour">The edges' colour; the theme's accent when not given.</param>
    public void Draw(UiPainter painter, EditorTheme theme, in ViewportProjector projector, in Box box, bool handles, Bgra32? colour = null)
    {
        DrawEdges(painter, theme, projector, box, colour ?? theme.Accent.WithAlpha(220));
        if (handles)
        {
            DrawHandles(painter, theme, projector, box);
        }
    }

    /// <summary>Draws a box's twelve edges.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="projector">The view.</param>
    /// <param name="box">The box.</param>
    /// <param name="colour">The edges' colour.</param>
    public static void DrawEdges(UiPainter painter, EditorTheme theme, in ViewportProjector projector, in Box box, Bgra32 colour)
    {
        Span<UiVec2> corners = stackalloc UiVec2[8];
        Span<bool> shown = stackalloc bool[8];
        for (int i = 0; i < 8; i++)
        {
            shown[i] = projector.TryProject(box.Corner(i), out corners[i]);
        }

        // Each corner joins the three that differ from it in one bit, counted once from the lower index.
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                int j = i | bit;
                if (j != i && shown[i] && shown[j])
                {
                    painter.StrokeLine(corners[i], corners[j], theme.Px(1.5f), colour);
                }
            }
        }
    }

    private void DrawHandles(UiPainter painter, EditorTheme theme, in ViewportProjector projector, in Box box)
    {
        for (int face = 0; face < 6; face++)
        {
            if (projector.TryProject(box.FaceCentre(face), out UiVec2 at))
            {
                bool hot = face == Hot;
                painter.FillDot(at, theme.Px(hot ? 7f : 5f), hot ? Bgra32.FromRgb(255, 222, 90) : theme.AxisColor(face / 2));
            }
        }
    }

    private static int FaceAt(in ViewportProjector projector, in Box box, UiVec2 point)
    {
        int best = -1;
        float bestDistance = GrabPixels;
        for (int face = 0; face < 6; face++)
        {
            if (projector.TryProject(box.FaceCentre(face), out UiVec2 at))
            {
                float distance = UiVec2.Distance(at, point);
                if (distance <= bestDistance)
                {
                    best = face;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    private static double Component(Vector3d v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>A box in the world: its centre, its turn and half its size along each of its axes, in metres.</summary>
    /// <param name="Centre">The centre.</param>
    /// <param name="Orientation">The task's orientation.</param>
    /// <param name="Half">The half sizes.</param>
    public readonly record struct Box(Vector3d Centre, Matrix3f Orientation, Vector3d Half)
    {
        /// <summary>Gets a corner: bit 0 picks −X, bit 1 −Y, bit 2 −Z.</summary>
        /// <param name="index">The corner, 0–7.</param>
        /// <returns>The corner, in world metres.</returns>
        public Vector3d Corner(int index) => TaskVolume.ToWorld(Centre, Orientation, new Vector3d(
            (index & 1) == 0 ? Half.X : -Half.X,
            (index & 2) == 0 ? Half.Y : -Half.Y,
            (index & 4) == 0 ? Half.Z : -Half.Z));

        /// <summary>Gets the middle of a face.</summary>
        /// <param name="face">The face, 0–5.</param>
        /// <returns>The point, in world metres.</returns>
        public Vector3d FaceCentre(int face) => Centre + (FaceDirection(face) * Component(Half, face / 2));

        /// <summary>Gets a face's outward direction in the world.</summary>
        /// <param name="face">The face, 0–5.</param>
        /// <returns>The unit direction.</returns>
        public Vector3d FaceDirection(int face)
        {
            double sign = (face & 1) == 0 ? 1.0 : -1.0;
            Vector3d local = (face / 2) switch
            {
                0 => new Vector3d(sign, 0, 0),
                1 => new Vector3d(0, sign, 0),
                _ => new Vector3d(0, 0, sign),
            };
            return TaskVolume.ToWorld(Vector3d.Zero, Orientation, local);
        }
    }
}
