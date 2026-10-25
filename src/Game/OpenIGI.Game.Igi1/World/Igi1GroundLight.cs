using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The light a Project I.G.I. level bakes into its ground, read at a point: what another game's rules see a human stand in, as
/// I.G.I. 2 reads its own player's from the lightmap under its feet.
/// </summary>
/// <remarks>
/// <para>
/// The level's data, not its picture: the <c>terrain.lmp</c> page the innermost <c>TerrainLightMap</c> cell over the point binds,
/// its texel through the ramp the level's sun and terrain ambient build (<see cref="TerrainLightBindings"/>). Every graphics
/// preset -- the original's lightmaps, real-time light and lamps -- sees the same guards.
/// </para>
/// <para>
/// The two games' light is matched by what it shows. I.G.I. 2 stores a texel's light as a byte times 2/255
/// (<c>Igi2TerrainVertex</c>) and draws it doubled (<c>MODULATE2X</c>), so one shows the ground as bright as its texture and two
/// twice as bright. Project I.G.I. multiplies its ground by the ramp's colour once (<c>sub_5144E0</c>, <c>ZERO, SRCCOLOR</c>):
/// its brightest texel shows the texture as it is, I.G.I. 2's one. The ramp's brightness, nought to one, is the light as it is.
/// </para>
/// </remarks>
public sealed class Igi1GroundLight : IBakedLight
{
    private readonly TerrainLightmaps _pages;
    private readonly TerrainLightBindings _bindings;

    private Igi1GroundLight(TerrainLightmaps pages, TerrainLightBindings bindings)
    {
        _pages = pages;
        _bindings = bindings;
    }

    /// <summary>Reads a level's ground light, or <see langword="null"/> where the level bakes none.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="levelRoot">The level, whose tasks bind the pages and light the ramp.</param>
    /// <returns>The ground light.</returns>
    public static Igi1GroundLight? Of(TerrainData? terrain, GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        if (terrain?.Lightmaps is not { } pages)
        {
            return null;
        }

        TerrainLightBindings bindings = TerrainLightBindings.Build(terrain, levelRoot);
        return bindings.IsEmpty ? null : new Igi1GroundLight(pages, bindings);
    }

    /// <inheritdoc/>
    /// <remarks>Only the point's X and Y are read; nothing is answered where no page is bound.</remarks>
    public float? LightAt(Vector3d point)
    {
        foreach (TerrainLightBindings.Entry entry in _bindings.Entries)
        {
            if (!entry.Contains(point))
            {
                continue;
            }

            TerrainLightmapPage page = _pages.Pages[entry.Page];
            (double u, double v) = entry.Binding.Project(point.X, point.Y);
            int column = Math.Clamp((int)(u * page.Size), 0, page.Size - 1);
            int row = Math.Clamp((int)(v * page.Size), 0, page.Size - 1);
            Bgra32 lit = _bindings.Ramp[page.Texels.Span[(row * page.Size) + column]];
            return (lit.R + lit.G + lit.B) / (3f * 255f);
        }

        return null;
    }
}
