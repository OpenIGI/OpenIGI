using OpenIGI.Core;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Tools.ResExtract;

/// <summary>
/// Finds every picture in an installation and writes it out as a PNG, with a manifest saying where each
/// one came from.
/// </summary>
/// <remarks>
/// <para>
/// WHAT COUNTS AS EVERY PICTURE, established by scanning a retail installation for the <c>"LOOP"</c>
/// magic at four-byte alignment rather than by trusting a list of extensions. There are 8,232 of them in
/// 7,388 containers, and they are in exactly two places: inside <c>.res</c> archives, and in loose
/// <c>.tex</c> files. Nothing in <c>.fnt</c>, <c>.mtp</c>, <c>.iff</c>, <c>.dat</c> or <c>.qvm</c>.
/// </para>
/// <para>
/// The earlier version of this command found 7,199 of them — the single-image entries inside archives —
/// and silently skipped the rest, which is why an upscale pack visibly improved the world and left the
/// menu, the loading screens, the HUD, the computer terminals, the sprites and every square metre of
/// ground exactly as they were.
/// </para>
/// <para>
/// A TILED PICTURE IS EXTRACTED WHOLE. <c>mainmenu.pic</c> is 150 frames of 64x32 laid out ten across
/// and fifteen down, which is one 640x480 picture. Upscaling those frames one at a time would put a seam
/// every 64 pixels across the menu, so the grid is assembled here and split again on the way back. The
/// test for "a picture rather than an animation" is the grid itself: the five <c>.pic</c> files are two
/// dimensional, and every <c>.spr</c> with a grid is N by 1 — a strip of animation frames, which must
/// stay apart because they are not a picture between them.
/// </para>
/// </remarks>
internal static class ImageExport
{
    /// <summary>What one run found.</summary>
    /// <param name="Assets">How many pictures were written.</param>
    /// <param name="Frames">How many PNG files that came to.</param>
    /// <param name="Repeats">How many copies of an already-written path were passed over.</param>
    /// <param name="Failures">What could not be decoded.</param>
    internal readonly record struct Result(int Assets, int Frames, int Repeats, List<string> Failures);

    /// <summary>Walks a directory or a single archive and writes every picture in it.</summary>
    /// <param name="source">The installation root, or one archive or loose texture.</param>
    /// <param name="destination">Where to write the PNG tree and the manifest.</param>
    /// <returns>What was found.</returns>
    internal static Result Run(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        bool directory = Directory.Exists(source);
        PackManifest manifest = new() { Source = Path.GetFullPath(source) };

        // Keyed by the path a replacement goes at, which is unique where a NAME is not: most of this
        // game's art is packed into every level that draws it, and 79 of the 1,363 names are shared by
        // files that differ. First writer wins, matching the registry's own first-registration rule.
        HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
        List<string> failures = [];
        int frames = 0;
        int repeats = 0;

        foreach (string path in Sources(source, directory))
        {
            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException error)
            {
                failures.Add($"{path}: {error.Message}");
                continue;
            }

            string relative = directory
                ? ResourcePath.Normalize(Path.GetRelativePath(source, path))
                : ResourcePath.Normalize(Path.GetFileName(path));

            if (IlffContainer.IsIlff(bytes))
            {
                ExportArchive(bytes, relative, destination, manifest, written, failures, ref frames, ref repeats);
            }
            else if (TexFile.IsLoopAsset(bytes))
            {
                // A loose picture is addressed by its own path: the terrain atlases, the sky layers, the
                // lens flares and the blob shadows, none of which is in an archive at all.
                Export(bytes, OverridePath.For(relative), archive: null, entry: null,
                    destination, manifest, written, failures, ref frames, ref repeats);
            }
        }

        manifest.Write(Path.Combine(destination, PackManifest.FileName));
        return new Result(manifest.Assets.Count, frames, repeats, failures);
    }

    private static IEnumerable<string> Sources(string source, bool directory)
    {
        if (!directory)
        {
            return [source];
        }

        // Both halves of the corpus, in one ordered walk so a run is reproducible.
        return Directory
            .EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".res", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    private static void ExportArchive(
        byte[] bytes,
        string relative,
        string destination,
        PackManifest manifest,
        HashSet<string> written,
        List<string> failures,
        ref int frames,
        ref int repeats)
    {
        IresArchive archive;

        try
        {
            IlffContainer container = IlffContainer.Read(bytes);

            if (container.FormType != IlffTags.Ires)
            {
                // A model or font container. Neither holds a LOOP picture in any retail file.
                return;
            }

            archive = IresArchive.FromContainer(container);
        }
        catch (IlffFormatException error)
        {
            failures.Add($"{relative}: {error.Message}");
            return;
        }

        foreach (IresEntry entry in archive.Entries)
        {
            if (entry.Kind != IresEntryKind.Body || !TexFile.IsLoopAsset(entry.Data.Span))
            {
                continue;
            }

            Export(
                entry.Data.ToArray(),
                OverridePath.For(relative, entry.Name),
                relative,
                entry.Name,
                destination,
                manifest,
                written,
                failures,
                ref frames,
                ref repeats);
        }
    }

    private static void Export(
        byte[] bytes,
        string overridePath,
        string? archive,
        string? entry,
        string destination,
        PackManifest manifest,
        HashSet<string> written,
        List<string> failures,
        ref int frames,
        ref int repeats)
    {
        if (overridePath.Length == 0)
        {
            return;
        }

        if (!written.Add(overridePath))
        {
            repeats++;
            return;
        }

        try
        {
            PackAsset asset = Decode(bytes, overridePath, archive, entry, destination);
            manifest.Assets.Add(asset);
            frames += asset.Frames.Count;
        }
        catch (Exception error) when (error is TextureFormatException or IlffFormatException or ArgumentException)
        {
            written.Remove(overridePath);
            failures.Add($"{overridePath}: {error.Message}");
        }
    }

    private static PackAsset Decode(
        byte[] bytes,
        string overridePath,
        string? archive,
        string? entry,
        string destination)
    {
        string stem = StripExtension(overridePath);

        if (TexFile.IsTexture(bytes))
        {
            TextureImage image = TexFile.Load(bytes);
            PackFrame frame = WriteFrame(destination, $"{stem}.png", image.GetLevel(0), image.Width, image.Height);

            return new PackAsset(
                overridePath, PackShape.Texture, image.Width, image.Height, [frame], archive, entry);
        }

        SpriteAsset sprite = SpriteLoader.Load(bytes, overridePath);

        // Two dimensions in both directions means the frames are tiles of one picture. One dimension
        // means a strip of animation frames that only happens to carry a grid.
        if (sprite.TileLayout is { Columns: > 1, Rows: > 1 } grid)
        {
            return Assemble(sprite, grid, overridePath, archive, entry, destination, stem);
        }

        List<PackFrame> written = [];

        for (int i = 0; i < sprite.Textures.Count; i++)
        {
            TextureImage image = sprite.Textures[i];
            SpriteFrame meta = sprite.Frames[i];

            written.Add(WriteFrame(
                destination,
                sprite.Textures.Count == 1 ? $"{stem}.png" : $"{stem}/{i:D3}.png",
                image.GetLevel(0),
                image.Width,
                image.Height,
                meta.Advance,
                meta.OffsetX,
                meta.OffsetY));
        }

        // A ground atlas is indexed, not animated, and the distinction is worth keeping in the manifest
        // even though both come back as a list of frames: it says what a reordering would break.
        PackShape shape = overridePath.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)
            ? PackShape.Atlas
            : PackShape.Sprite;

        return new PackAsset(
            overridePath,
            shape,
            sprite.DefaultWidth,
            sprite.DefaultHeight,
            written,
            archive,
            entry,
            sprite.TileLayout?.Columns ?? 0,
            sprite.TileLayout?.Rows ?? 0);
    }

    private static PackAsset Assemble(
        SpriteAsset sprite,
        SpriteTileLayout grid,
        string overridePath,
        string? archive,
        string? entry,
        string destination,
        string stem)
    {
        int tileWidth = sprite.Textures[0].Width;
        int tileHeight = sprite.Textures[0].Height;
        int width = grid.Columns * tileWidth;
        int height = grid.Rows * tileHeight;

        Bgra32[] picture = new Bgra32[width * height];

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                int index = grid.GetFrameIndex(column, row);

                // A cell can be empty, and the assembled picture keeps the hole rather than inventing
                // something to fill it. Splitting it again on the way back reproduces the same hole.
                if (index < 0 || index >= sprite.Textures.Count)
                {
                    continue;
                }

                TextureImage tile = sprite.Textures[index];
                ReadOnlySpan<Bgra32> pixels = tile.GetLevel(0);

                for (int y = 0; y < tileHeight && y < tile.Height; y++)
                {
                    int from = y * tile.Width;
                    int to = (((row * tileHeight) + y) * width) + (column * tileWidth);
                    pixels.Slice(from, Math.Min(tile.Width, tileWidth)).CopyTo(picture.AsSpan(to));
                }
            }
        }

        PackFrame frame = WriteFrame(destination, $"{stem}.png", picture, width, height);

        return new PackAsset(
            overridePath,
            PackShape.Picture,
            width,
            height,
            [frame],
            archive,
            entry,
            grid.Columns,
            grid.Rows);
    }

    private static PackFrame WriteFrame(
        string destination,
        string relative,
        ReadOnlySpan<Bgra32> pixels,
        int width,
        int height,
        int advance = 0,
        int offsetX = 0,
        int offsetY = 0)
    {
        string path = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PngWriter.Write(path, pixels, width, height);

        return new PackFrame(relative, width, height, advance, offsetX, offsetY);
    }

    private static string StripExtension(string path)
    {
        int dot = path.LastIndexOf('.');
        int slash = path.LastIndexOf('/');
        return dot > slash ? path[..dot] : path;
    }
}
