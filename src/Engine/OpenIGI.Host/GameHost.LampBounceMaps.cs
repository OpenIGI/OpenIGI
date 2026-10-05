using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>The surface cache's coarse lamp maps: six small faces for every lamp in the level.</summary>
/// <remarks>
/// Drawn a few faces a frame after a level loads, nearest lamps first, and kept: they hold the static world,
/// which draws the same picture every time. The relight reads a lamp's six once all six are drawn and marches
/// the distance field for it until then. See <see cref="IGraphicsDevice.LampBounceMapSlots"/>.
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>How many coarse faces are drawn a frame until every lamp has its six.</summary>
    /// <remarks>Sixty-four texels a side each: a dozen cost less than one of the shadow maps' new faces.</remarks>
    private const int LampBounceFacesPerFrame = 12;

    // Per fixture: how many of its six faces are drawn, and what the relight is told (the first face, or -1).
    private int[] _lampBounceFaces = [];
    private int[] _lampBounceEntries = [];
    private int[] _lampBounceOrder = [];
    private IReadOnlyList<LightFixture>? _lampBounceFixtures;

    // The device's count as of the last face drawn here; anything else means it let the maps go.
    private int _lampBounceVersion = -1;

    /// <summary>Draws the next few coarse faces, and tells the relight which lamps have all six.</summary>
    /// <param name="pass">The caster pass, which draws through the title's presenters.</param>
    /// <param name="scene">The world's placed objects.</param>
    /// <param name="world">The presenter that draws them.</param>
    private void RenderLampBounceMaps(ShadowCasterPass pass, WorldScene? scene, IWorldPresenter? world)
    {
        int slots = _device.LampBounceMapSlots;
        if (slots <= 0 || _cardLighting is not { } cards)
        {
            return;
        }

        // A new level, or the device let the maps go (a settings change rebuilds the shadow resources):
        // everything is drawn again.
        if (!ReferenceEquals(_lampBounceFixtures, _lightFixtures)
            || _lampBounceFaces.Length != _lightFixtures.Count
            || _device.LampBounceMapVersion != _lampBounceVersion)
        {
            ResetLampBounceMaps();
        }

        int lamps = Math.Min(_lightFixtures.Count, slots / CubeFaceAxes.Length);
        int drawn = 0;
        for (int k = 0; k < lamps && drawn < LampBounceFacesPerFrame; k++)
        {
            int fixture = _lampBounceOrder[k];
            while (_lampBounceFaces[fixture] < CubeFaceAxes.Length && drawn < LampBounceFacesPerFrame)
            {
                int face = _lampBounceFaces[fixture];
                (Vector3d from, Matrix4f projection) = LampMapView(_lightFixtures[fixture]);
                if (!pass.RenderLampMap(
                    (k * CubeFaceAxes.Length) + face,
                    from,
                    ShadowCascades.LightRotationFor(-CubeFaceAxes[face]),
                    projection,
                    scene,
                    world,
                    characters: null,
                    coarse: true))
                {
                    return;
                }

                _lampBounceVersion = _device.LampBounceMapVersion;
                _lampBounceFaces[fixture]++;
                drawn++;
                if (_lampBounceFaces[fixture] == CubeFaceAxes.Length)
                {
                    _lampBounceEntries[fixture] = k * CubeFaceAxes.Length;
                }
            }
        }

        cards.SetLampMapEntries(_lampBounceEntries);
    }

    // Each of the frame's lamps' coarse cube, in light order, for the device.
    private readonly int[] _lampCoarsePerLight = new int[DeferredLocalLight.Maximum];

    /// <summary>Tells the device which coarse cube each of this frame's lamps has.</summary>
    /// <param name="lightCount">How many lamps the frame was given.</param>
    /// <remarks>
    /// For the lamp shadows of the lamps the fine maps do not reach: past the fine maps' budget a lamp was
    /// answered by the distance field alone, which at a distance cannot hold the wall it stands behind.
    /// </remarks>
    private void AssignLampCoarseMaps(int lightCount)
    {
        int count = Math.Min(lightCount, _lampCoarsePerLight.Length);
        for (int i = 0; i < count; i++)
        {
            int fixture = _localLightOrder[i];
            _lampCoarsePerLight[i] = (uint)fixture < (uint)_lampBounceEntries.Length ? _lampBounceEntries[fixture] : -1;
        }

        _device.SetLampCoarseMapSlots(_lampCoarsePerLight.AsSpan(0, count));
    }

    /// <summary>Forgets every coarse face and orders the lamps nearest the camera first.</summary>
    private void ResetLampBounceMaps()
    {
        int count = _lightFixtures.Count;
        _lampBounceFixtures = _lightFixtures;
        _lampBounceFaces = new int[count];
        _lampBounceEntries = new int[count];
        Array.Fill(_lampBounceEntries, -1);
        _lampBounceVersion = _device.LampBounceMapVersion;

        // Nearest first, so the lamps around the player are right within a few frames of the load; the
        // order is fixed then, since a lamp's slots are where it sits in it.
        Vector3d eye = _camera?.Position ?? default;
        _lampBounceOrder = [.. Enumerable.Range(0, count).OrderBy(i =>
        {
            Vector3d at = _lightFixtures[i].Position;
            double dx = at.X - eye.X;
            double dy = at.Y - eye.Y;
            double dz = at.Z - eye.Z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        })];
    }
}
