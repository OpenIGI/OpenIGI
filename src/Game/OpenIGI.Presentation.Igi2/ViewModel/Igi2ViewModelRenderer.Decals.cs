using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Presentation.Igi2.ViewModel;

/// <content>Marks lying on the ground: a body's shadow and its pool of blood (<c>Shadow_MagicObjEnterHandler</c>).</content>
public sealed partial class Igi2ViewModelRenderer
{
    private GroundDecalDrawer? _decals;

    /// <inheritdoc/>
    /// <remarks>
    /// A square in the ground's own plane (<c>Real32x9_FromReal32x3</c> of its normal), its half-width either way of the point and
    /// a hover above it, blended over the world and tested against its depth, written into none.
    /// </remarks>
    public void DrawGroundDecals(IReadOnlyList<TitleGroundDecal> decals, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(decals);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_textures is not { } textures || decals.Count == 0)
        {
            return;
        }

        _decals ??= new GroundDecalDrawer(_device);
        _decals.Draw(decals, textures.Resolve, camera, projection);
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
        _decals?.Dispose();
        _decals = null;
        _polygons?.Dispose();
        _polygons = null;
    }
}
