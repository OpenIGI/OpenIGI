using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Tools.ResExtract;

/// <summary>
/// Turns a tree of worked-on PNGs back into the game's own files, laid out as an overrides folder.
/// </summary>
/// <remarks>
/// <para>
/// The other half of <see cref="ImageExport"/>, and the reason it writes a manifest. A PNG's path says
/// where its replacement goes; it does not say which archive the original came out of, whether it was
/// one of 150 tiles of a loading screen, or what size the menu lays it out at. The manifest says all
/// three, so this can put every picture back in the shape it was found in.
/// </para>
/// <para>
/// WHAT COMES OUT. An archived picture goes back into an archive at the archive's own path, holding only
/// the entries the pack replaces — an overlay, not a copy of the game's archive with some entries
/// swapped, so nothing falls out of the installation by being left out of a pack. A loose picture is
/// written at its own path. Either way the result is dropped into <c>&lt;game&gt;/overrides</c> and the
/// game finds it by the path it already had.
/// </para>
/// <para>
/// Inside those files every picture is a <see cref="PackPicture"/> rather than the container it came
/// from. That is what keeps a 4x pack down from about 3 GB, and it is what lets the menu stay the shape
/// it was: the logical size travels beside the pixels, so a picture four times the resolution still lays
/// out at the size the menu was built around.
/// </para>
/// <para>
/// A PICTURE THAT WAS A TILE GRID IS SPLIT AGAIN, into the same grid it was assembled from — the tiles
/// are just larger. Splitting is what makes it safe to have upscaled the whole picture: the seams the
/// network never saw are interior pixels now, and the grid the game reassembles is unchanged.
/// </para>
/// </remarks>
internal static class PackBuild
{
    /// <summary>What one run produced.</summary>
    /// <param name="Assets">How many pictures were written into the pack.</param>
    /// <param name="Archives">How many archives were built.</param>
    /// <param name="Loose">How many loose files were written.</param>
    /// <param name="Missing">How many manifest entries had no PNG to pack.</param>
    /// <param name="Bytes">How large the pack came out.</param>
    /// <param name="Failures">What could not be packed.</param>
    internal readonly record struct Result(
        int Assets, int Archives, int Loose, int Missing, long Bytes, List<string> Failures);

    /// <summary>How a pack is encoded.</summary>
    /// <param name="BlockCompress">
    /// Whether world textures are stored as GPU block-compressed chains (the default) rather than PNGs.
    /// Compressed, the game uploads them as they are: a quarter to an eighth of the memory and no decode.
    /// </param>
    /// <param name="Quality">How hard the block encoder searches.</param>
    internal readonly record struct Options(bool BlockCompress = true, BlockQuality Quality = BlockQuality.Balanced);

    /// <summary>Builds an overrides folder from an extracted, worked-on PNG tree.</summary>
    /// <param name="source">The PNG tree, holding the manifest the extractor wrote.</param>
    /// <param name="destination">Where to write the overrides folder.</param>
    /// <param name="options">How to encode it.</param>
    /// <returns>What was produced.</returns>
    internal static Result Run(string source, string destination, Options options = default)
    {
        PackManifest manifest = PackManifest.Read(Path.Combine(source, PackManifest.FileName));

        Directory.CreateDirectory(destination);

        // Entries accumulate per archive and are written once at the end, because an IRES archive is a
        // stream of chunks whose offsets depend on everything before them -- it cannot be appended to.
        Dictionary<string, List<IresWriter.Entry>> archives = new(StringComparer.OrdinalIgnoreCase);
        List<string> failures = [];
        int assets = 0;
        int loose = 0;
        int missing = 0;
        long bytes = 0;

        // Encoded side by side -- a block encoder is most of a second a texture -- and written in manifest
        // order afterwards, so the archives come out the same whatever order the workers finish in.
        PackAsset[] all = [.. manifest.Assets];
        Encoded[] encoded = new Encoded[all.Length];
        Parallel.For(0, all.Length, i => encoded[i] = EncodeAsset(source, all[i], options));

        for (int i = 0; i < all.Length; i++)
        {
            PackAsset asset = all[i];
            (byte[]? file, bool absent, string? failure) = encoded[i];

            if (absent)
            {
                missing++;
                continue;
            }

            if (failure is not null || file is null)
            {
                failures.Add($"{asset.Path}: {failure}");
                continue;
            }

            assets++;

            if (asset.Archive is { Length: > 0 } archive && asset.Entry is { Length: > 0 } entry)
            {
                if (!archives.TryGetValue(archive, out List<IresWriter.Entry>? list))
                {
                    archives[archive] = list = [];
                }

                list.Add(new IresWriter.Entry(entry, IresEntryKind.Body, file));
                continue;
            }

            bytes += Write(destination, asset.Path, file);
            loose++;
        }

        foreach ((string archive, List<IresWriter.Entry> entries) in archives)
        {
            try
            {
                bytes += Write(destination, archive, IresWriter.Write(entries));
            }
            catch (Exception error) when (error is IlffFormatException or IOException)
            {
                failures.Add($"{archive}: {error.Message}");
            }
        }

        return new Result(assets, archives.Count, loose, missing, bytes, failures);
    }

    /// <summary>One asset's encoded file, or why there is none.</summary>
    private readonly record struct Encoded(byte[]? File, bool Absent, string? Failure);

    private static Encoded EncodeAsset(string source, PackAsset asset, Options options)
    {
        try
        {
            if (!TryLoad(source, asset, out List<PackPicture.Frame> frames))
            {
                return new Encoded(null, true, null);
            }

            // Only a world texture: one picture, no grid. Sprites, atlases and tile grids are laid out by
            // their frames' pixels on the CPU, and stay PNG.
            if (options.BlockCompress
                && asset.Shape == PackShape.Texture
                && frames.Count == 1
                && PackCompression.CanCompress(frames[0].Image))
            {
                frames[0] = PackCompression.Compress(frames[0], options.Quality);
            }

            return new Encoded(Encode(asset, frames), false, null);
        }
        catch (Exception error) when (error is TextureFormatException or ArgumentException or IOException)
        {
            return new Encoded(null, false, error.Message);
        }
    }

    private static bool TryLoad(string source, PackAsset asset, out List<PackPicture.Frame> frames)
    {
        frames = [];

        foreach (PackFrame frame in asset.Frames)
        {
            string path = Path.Combine(source, frame.File.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                // A pack built from a half-finished upscale is a useful thing, not an error: what is
                // there is packed and what is not is left to the game's own copy.
                return false;
            }

            (Bgra32[] pixels, int width, int height) = PngReader.Read(File.ReadAllBytes(path));

            frames.Add(new PackPicture.Frame(
                TextureImage.FromPixels(width, height, pixels),
                frame.Width,
                frame.Height,
                frame.Advance,
                frame.OffsetX,
                frame.OffsetY,
                Surface(path)));
        }

        return frames.Count > 0;
    }

    /// <summary>Picks up the derived surface map beside a picture, when whatever made it left one.</summary>
    /// <remarks>
    /// By convention rather than by manifest: a picture at <c>x.png</c> takes its map from <c>x.orm.png</c>.
    /// The manifest describes the GAME -- what each picture is and where it came from -- and a derived map
    /// is not that; it is something a tool chose to produce, and a tool that produces one should not have
    /// to rewrite a description of the installation to say so.
    /// </remarks>
    private static TextureImage? Surface(string picture)
    {
        string path = Path.ChangeExtension(picture, null) + ".orm.png";

        if (!File.Exists(path))
        {
            return null;
        }

        (Bgra32[] pixels, int width, int height) = PngReader.Read(File.ReadAllBytes(path));
        return TextureImage.FromPixels(width, height, pixels);
    }

    private static byte[] Encode(PackAsset asset, List<PackPicture.Frame> frames)
    {
        if (asset.Shape != PackShape.Picture)
        {
            return PackPicture.Write(
                new PackPicture.Content(
                    frames,
                    asset.LogicalWidth,
                    asset.LogicalHeight,
                    asset.GridColumns,
                    asset.GridRows,
                    SequentialCells(asset.GridColumns * asset.GridRows, frames.Count)));
        }

        PackPicture.Frame whole = frames[0];
        List<PackPicture.Frame> tiles = Split(whole, asset);

        return PackPicture.Write(
            new PackPicture.Content(
                tiles,
                asset.LogicalWidth,
                asset.LogicalHeight,
                asset.GridColumns,
                asset.GridRows,
                SequentialCells(tiles.Count, tiles.Count)));
    }

    private static List<PackPicture.Frame> Split(in PackPicture.Frame whole, PackAsset asset)
    {
        TextureImage image = whole.Image;
        int columns = asset.GridColumns;
        int rows = asset.GridRows;

        if (image.Width % columns != 0 || image.Height % rows != 0)
        {
            throw new TextureFormatException(
                $"A {image.Width}x{image.Height} picture does not divide into a {columns}x{rows} grid; "
                    + "the upscale has to keep the whole picture a whole multiple of its grid.");
        }

        int tileWidth = image.Width / columns;
        int tileHeight = image.Height / rows;

        // The size the game lays a tile out at, which is the original tile size and not this one.
        int logicalWidth = Math.Max(1, asset.LogicalWidth / columns);
        int logicalHeight = Math.Max(1, asset.LogicalHeight / rows);

        ReadOnlySpan<Bgra32> pixels = image.GetLevel(0);
        List<PackPicture.Frame> tiles = new(columns * rows);

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Bgra32[] tile = new Bgra32[tileWidth * tileHeight];

                for (int y = 0; y < tileHeight; y++)
                {
                    int from = (((row * tileHeight) + y) * image.Width) + (column * tileWidth);
                    pixels.Slice(from, tileWidth).CopyTo(tile.AsSpan(y * tileWidth));
                }

                tiles.Add(new PackPicture.Frame(
                    TextureImage.FromPixels(tileWidth, tileHeight, tile), logicalWidth, logicalHeight));
            }
        }

        return tiles;
    }

    /// <summary>Builds a cell table that names each frame once, in reading order.</summary>
    /// <remarks>
    /// The original cell table does not survive assembly and does not need to: once a picture has been
    /// worked on whole, where each tile came from is no longer interesting, only where it goes. Cells are
    /// one-based, and zero means an empty cell.
    /// </remarks>
    private static byte[] SequentialCells(int cells, int frames)
    {
        if (cells <= 0)
        {
            return [];
        }

        byte[] table = new byte[cells];

        for (int i = 0; i < cells; i++)
        {
            table[i] = i < frames ? (byte)(i + 1) : (byte)0;
        }

        return table;
    }

    private static long Write(string destination, string relative, byte[] bytes)
    {
        string path = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return bytes.Length;
    }
}
