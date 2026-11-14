using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <content>The glows of the models the eye's picture drew, gathered for whatever draws them.</content>
public sealed partial class WorldRenderer
{
    private readonly Dictionary<MefModel, MefGlow[]> _glowsOf = [];
    private readonly List<WorldGlow> _drawnGlows = [];

    /// <inheritdoc/>
    public bool CollectGlows { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<WorldGlow> DrawnGlows => _drawnGlows;

    /// <summary>Adds the glows of one model the eye is about to be shown, where the frame stands it.</summary>
    /// <remarks>
    /// The original queues a sprite for every glow of every mesh it draws, at the detail level it draws it
    /// (<c>Direct3DRender</c>'s rigid and spline-segment paths), so a glow is seen exactly when its model
    /// is. A spline segment's glows bend with its mesh there; the bend is not reproduced, so those are
    /// left out rather than drawn beside their lamps.
    /// </remarks>
    private void GatherGlows(WorldPlacement displayed, in WorldFrameEntry entry)
    {
        if (displayed.BentGeometry is not null || entry.HasRotor)
        {
            return;
        }

        if (!_glowsOf.TryGetValue(displayed.Model, out MefGlow[]? glows))
        {
            glows = [.. displayed.Model.Glows];
            _glowsOf[displayed.Model] = glows;
        }

        if (glows.Length == 0)
        {
            return;
        }

        Matrix3f drawn = entry.Orientation.Transposed();
        Vector3d origin = entry.Position * WorldScene.UnitsPerMetre;

        foreach (MefGlow glow in glows)
        {
            Vector3d turned = drawn.Transform(new Vector3d(glow.Position.X, glow.Position.Y, glow.Position.Z));

            // The colour as the original packs it into the vertex: each channel times 255, cut to a byte.
            _drawnGlows.Add(new WorldGlow(
                origin + turned,
                glow.Radius,
                new Bgra32(Channel(glow.Colour.Z), Channel(glow.Colour.Y), Channel(glow.Colour.X), 255)));
        }
    }

    private static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);
}
