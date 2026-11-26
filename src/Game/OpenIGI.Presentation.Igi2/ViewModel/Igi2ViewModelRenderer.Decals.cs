using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Presentation.Igi2.ViewModel;

/// <content>
/// Marks laid on surfaces: a body's shadow and its pool of blood (<c>Shadow_MagicObjEnterHandler</c>), and a laser sight's dot
/// (<c>LaserDot_EnterHandler</c>).
/// </content>
public sealed partial class Igi2ViewModelRenderer
{
    private SurfaceMarkDrawer? _marks;

    /// <inheritdoc/>
    /// <remarks>
    /// A square in the surface's own plane (<c>Real32x9_FromReal32x3</c> of its normal), its half-width either way of the point and
    /// a hover off it, blended over the world, or added to it, and tested against its depth, written into none.
    /// </remarks>
    public void DrawSurfaceMarks(IReadOnlyList<TitleSurfaceMark> marks, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(marks);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_textures is not { } textures || marks.Count == 0)
        {
            return;
        }

        _marks ??= new SurfaceMarkDrawer(_device);
        _marks.Draw(marks, textures.Resolve, camera, projection);
    }

    private FlatPolygonDrawer? _polygons;

    /// <inheritdoc/>
    /// <remarks>The birds of the level's flocks, as a flock's <c>PolyListDynCubeObj</c> draws them, flat shaded.</remarks>
    public void DrawWorldPolygons(IReadOnlyList<TitleWorldPolygons> polygons, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (polygons.Count == 0)
        {
            return;
        }

        _polygons ??= new FlatPolygonDrawer(_device);
        _polygons.Draw(polygons, camera, projection);
    }

    private void ReleaseDecals()
    {
        _marks?.Dispose();
        _marks = null;
        _polygons?.Dispose();
        _polygons = null;
    }
}
