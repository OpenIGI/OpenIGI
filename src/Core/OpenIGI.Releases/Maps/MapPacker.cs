namespace OpenIGI.Releases.Maps;

/// <summary>
/// Makes a map's package from its mission's files, and puts a package's files back: a file that is one of the game's own,
/// byte for byte, is named rather than carried, and the player's own game gives it back.
/// </summary>
public static class MapPacker
{
    /// <summary>Makes a map's manifest from its mission's files.</summary>
    /// <param name="about">What the map says of itself; its <see cref="MapManifest.Files"/> are made here.</param>
    /// <param name="files">Every file of the mission's folder, by path from the game's root, with its bytes.</param>
    /// <param name="gameFile">The game's own file with a hash, by its path, or <see langword="null"/> when the game has none.</param>
    /// <returns>The manifest, its files in ordinal order.</returns>
    public static MapManifest Manifest(MapManifest about, IEnumerable<(string Path, ReadOnlyMemory<byte> Bytes)> files, Func<string, string?> gameFile)
    {
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(gameFile);
        List<MapFile> listed = [];
        foreach ((string path, ReadOnlyMemory<byte> bytes) in files.OrderBy(static f => f.Path, StringComparer.Ordinal))
        {
            string hash = MapPackage.Hash(bytes.Span);
            listed.Add(new MapFile(path.Replace('\\', '/'), bytes.Length, hash, gameFile(hash)));
        }

        return about with { Schema = MapManifest.CurrentSchema, Files = listed };
    }

    /// <summary>Puts a package's files where the map is played from.</summary>
    /// <param name="package">The package.</param>
    /// <param name="readGameFile">Reads one of the game's own files by its path from the game's root; null when the game has none there.</param>
    /// <param name="write">Writes a file, by its path from the game's root.</param>
    /// <returns>The map's manifest.</returns>
    /// <exception cref="InvalidDataException">The package is not a map, breaks the rules, or names a game file the player's game does not have as it was.</exception>
    public static MapManifest Unpack(ReadOnlyMemory<byte> package, Func<string, ReadOnlyMemory<byte>?> readGameFile, Action<string, ReadOnlyMemory<byte>> write)
    {
        ArgumentNullException.ThrowIfNull(readGameFile);
        ArgumentNullException.ThrowIfNull(write);
        (MapManifest manifest, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> carried) = MapPackage.Read(package);
        if (MapRules.Check(manifest) is { Count: > 0 } problems)
        {
            throw new InvalidDataException($"The map breaks the rules: {problems[0]}");
        }

        foreach (MapFile file in manifest.Files)
        {
            ReadOnlyMemory<byte> bytes;
            if (file.Origin is { } origin)
            {
                if (readGameFile(origin) is not { } own || MapPackage.Hash(own.Span) != file.Sha256)
                {
                    throw new InvalidDataException($"The map uses the game's {origin}, which this installation does not have as the map's author did.");
                }

                bytes = own;
            }
            else
            {
                bytes = carried[file.Path];
            }

            write(file.Path, bytes);
        }

        return manifest;
    }
}
