using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// One drawable surface as it arrives from the resource system: the picture, and the derived map that
/// travels with it when the asset carries one.
/// </summary>
/// <param name="Diffuse">The colour the surface is drawn with.</param>
/// <param name="Detail">
/// The packed normal, roughness and occlusion map derived for it, or <see langword="null"/> when the
/// asset carries none — which is every texture the game itself ships, and every replacement built without
/// them.
/// </param>
/// <remarks>
/// <para>
/// <b>Why a pair and not a texture.</b> A replacement asset in the overrides folder can hold its derived
/// surface map as a second layer of the same file. Keeping them together is the point: they are produced
/// together from the same source image and at the same resolution, so a format that lets them be
/// installed separately is a format that lets them disagree — a normal map for a texture that has since
/// been replaced is worse than no normal map at all, and nothing on screen would say which one is stale.
/// </para>
/// <para>
/// The game's own textures are single-image containers and always come back with no map, so a level made
/// entirely of them behaves exactly as it did before any of this existed.
/// </para>
/// </remarks>
public readonly record struct SurfaceAsset(TextureImage Diffuse, TextureImage? Detail);

/// <summary>Reads whichever container form a surface arrived in.</summary>
/// <remarks>
/// The resource system hands out bytes without saying where they came from — the game's own archive, a
/// loose file, or a replacement in the overrides folder — which is what makes an override transparent to
/// everything downstream. The cost of that is that the reader cannot assume a form, so it asks.
/// </remarks>
public static class SurfaceAssetReader
{
    /// <summary>The layer the first arrangement carried a derived surface map in.</summary>
    /// <remarks>
    /// Kept for the packs built before <see cref="PackPicture"/> existed. Putting the map in layer 1 of a
    /// multi-image container works only while nothing else means anything by layer 1 — and a ground atlas
    /// means its second tile, and a sprite means its second animation frame. A pack picture gives the map
    /// its own slot for that reason, and this is the compatibility path, not the one to write.
    /// </remarks>
    public const int DetailLayer = 1;

    /// <summary>Reads a surface, with its derived map when the asset carries one.</summary>
    /// <param name="data">The file bytes, from the resource system.</param>
    /// <param name="palette">The palette to use if the texture is paletted.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a usable texture.</exception>
    /// <remarks>
    /// <para>
    /// A multi-image container is a replacement: layer 0 is the picture and layer 1, when present, is the
    /// packed map. Anything else is a single texture, which is what the game ships.
    /// </para>
    /// <para>
    /// <b>A level's terrain atlas is also a multi-image container</b>, and is not read through here — it
    /// is read as a whole atlas, because its layers are eighteen different ground tiles rather than one
    /// picture and its map. The distinction is the caller's, not the file's: nothing in the format says
    /// what a layer means.
    /// </para>
    /// </remarks>
    public static SurfaceAsset Read(ReadOnlySpan<byte> data, ReadOnlySpan<Bgra32> palette = default)
    {
        // Asked first, because a pack picture answers to IsAtlas as well -- it stands in for every one
        // of the game's containers -- and reading one as an atlas would take its second FRAME for a
        // surface map. Its map is in a slot of its own and says so.
        if (PackPicture.Is(data))
        {
            PackPicture.Frame frame = PackPicture.Read(data.ToArray()).Frames[0];
            return new SurfaceAsset(frame.Image, frame.Surface);
        }

        if (!TexAtlasFile.IsAtlas(data))
        {
            return new SurfaceAsset(TexFile.Load(data, palette), null);
        }

        TexAtlasFile atlas = TexAtlasFile.Read(data.ToArray());

        if (atlas.Tiles.Count == 0)
        {
            throw new TextureFormatException("A replacement texture holds no layers.");
        }

        return new SurfaceAsset(
            atlas.Tiles[0],
            atlas.Tiles.Count > DetailLayer ? atlas.Tiles[DetailLayer] : null);
    }
}
