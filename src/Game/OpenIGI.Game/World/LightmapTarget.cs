using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One page of lightmap texels for one draw record of one placed object.</summary>
/// <remarks>
/// A record, not the model: a model's draw records can name different lightmap pages, and a page is
/// what an <c>.olm</c> image is. The grid is row-major, width fastest.
/// </remarks>
public sealed class LightmapTarget
{
    internal LightmapTarget(int placementIndex, int recordIndex, int page, int width, int height)
    {
        PlacementIndex = placementIndex;
        RecordIndex = recordIndex;
        Page = page;
        Width = width;
        Height = height;
        Texels = new LightmapTexel[width * height];
        Radiance = new Vector3f[width * height];
        Direct = new Vector3f[width * height];
    }

    /// <summary>The scene placement this page belongs to.</summary>
    public int PlacementIndex { get; }

    /// <summary>The draw record within the placement's model.</summary>
    public int RecordIndex { get; }

    /// <summary>The lightmap page index within the model.</summary>
    public int Page { get; }

    /// <summary>The page width in texels.</summary>
    public int Width { get; }

    /// <summary>The page height in texels.</summary>
    public int Height { get; }

    /// <summary>The texels, row-major, width fastest.</summary>
    public LightmapTexel[] Texels { get; }

    /// <summary>The light each texel receives, filled by the bake; parallel to <see cref="Texels"/>.</summary>
    /// <remarks>Direct sun plus sky after phase 2; the multi-bounce indirect is folded in by phase 3.</remarks>
    public Vector3f[] Radiance { get; }

    /// <summary>The direct sun term alone, kept so each bounce pass can re-add it rather than re-tracing it.</summary>
    public Vector3f[] Direct { get; }
}
