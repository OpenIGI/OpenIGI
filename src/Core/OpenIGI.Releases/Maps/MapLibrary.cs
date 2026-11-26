using System.Text.Json;

namespace OpenIGI.Releases.Maps;

/// <summary>
/// The community maps installed on this machine: a folder each under the user's <c>maps</c> folder, holding the package as it
/// was downloaded (<c>map.oigimap</c>), its listing (<c>listing.json</c>), and, once it has been played, its files unpacked
/// (<c>files/</c>, marked by <c>unpacked</c>, which names the package they came from).
/// </summary>
/// <remarks>
/// The launcher and the game both install here. Only the game unpacks: the game's own files a map names are read from the
/// game itself, which the launcher does not open.
/// </remarks>
/// <param name="root">The <c>maps</c> folder.</param>
public sealed class MapLibrary(string root)
{
    /// <summary>The name of the package in a map's folder.</summary>
    public const string PackageName = "map" + MapPackage.Extension;

    private const string ListingName = "listing.json";
    private const string FilesName = "files";
    private const string UnpackedName = "unpacked";

    /// <summary>Gets the <c>maps</c> folder.</summary>
    public string Root { get; } = root;

    /// <summary>Gets every installed map's listing, the newest installed first.</summary>
    /// <returns>The listings.</returns>
    public IReadOnlyList<MapListing> Installed()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        List<(MapListing Listing, DateTime At)> found = [];
        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            string listing = Path.Combine(folder, ListingName);
            if (File.Exists(listing) && File.Exists(Path.Combine(folder, PackageName)))
            {
                try
                {
                    if (JsonSerializer.Deserialize(File.ReadAllBytes(listing), MapJsonContext.Default.MapListing) is { } read)
                    {
                        found.Add((read, File.GetLastWriteTimeUtc(listing)));
                    }
                }
                catch (JsonException)
                {
                    // A listing that does not read is a map half installed: not offered.
                }
            }
        }

        return [.. found.OrderByDescending(static f => f.At).Select(static f => f.Listing)];
    }

    /// <summary>Gets an installed map's listing, or <see langword="null"/>.</summary>
    /// <param name="id">The map.</param>
    /// <returns>The listing.</returns>
    public MapListing? Find(string id) => Installed().FirstOrDefault(m => m.Id == id);

    /// <summary>Installs a map as downloaded, or a newer version over an older one.</summary>
    /// <param name="listing">Its listing.</param>
    /// <param name="package">Its package.</param>
    /// <exception cref="InvalidDataException">The package is not a map, or not the one listed.</exception>
    public void Install(MapListing listing, byte[] package)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(package);
        if (!ContentManifest.IsValidId(listing.Id) || MapPackage.ReadManifest(package, out _).Id != listing.Id)
        {
            throw new InvalidDataException("The package is not the map listed.");
        }

        string folder = Folder(listing.Id);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, PackageName + ".part"), package);
        File.Move(Path.Combine(folder, PackageName + ".part"), Path.Combine(folder, PackageName), overwrite: true);
        File.WriteAllBytes(Path.Combine(folder, ListingName), JsonSerializer.SerializeToUtf8Bytes(listing, MapJsonContext.Default.MapListing));
    }

    /// <summary>Removes an installed map.</summary>
    /// <param name="id">The map.</param>
    public void Remove(string id)
    {
        if (ContentManifest.IsValidId(id) && Directory.Exists(Folder(id)))
        {
            Directory.Delete(Folder(id), recursive: true);
        }
    }

    /// <summary>Gets the folder a map's files are played from, unpacking them first when they are not yet, or are an older version's.</summary>
    /// <param name="id">The map.</param>
    /// <param name="readGameFile">Reads one of the game's own files by its path from the game's root; null when the game has none.</param>
    /// <returns>The folder, and the map's manifest.</returns>
    /// <exception cref="InvalidDataException">The map is not installed, does not read, or names a game file this game does not have as its author did.</exception>
    public (string Files, MapManifest Manifest) Unpacked(string id, Func<string, ReadOnlyMemory<byte>?> readGameFile)
    {
        string folder = Folder(id);
        string package = Path.Combine(folder, PackageName);
        if (!ContentManifest.IsValidId(id) || !File.Exists(package))
        {
            throw new InvalidDataException($"The map {id} is not installed.");
        }

        byte[] bytes = File.ReadAllBytes(package);
        string hash = MapPackage.Hash(bytes);
        string files = Path.Combine(folder, FilesName);
        string marker = Path.Combine(folder, UnpackedName);
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == hash && Directory.Exists(files))
        {
            return (files, MapPackage.ReadManifest(bytes, out _));
        }

        if (Directory.Exists(files))
        {
            Directory.Delete(files, recursive: true);
        }

        string staging = files + ".part";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        MapManifest manifest = MapPacker.Unpack(bytes, readGameFile, (path, content) =>
        {
            string to = Path.GetFullPath(Path.Combine(staging, path));
            if (!to.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{path} is not inside the map.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.WriteAllBytes(to, content.ToArray());
        });
        Directory.Move(staging, files);
        File.WriteAllText(marker, hash);
        return (files, manifest);
    }

    private string Folder(string id) => Path.Combine(Root, id);
}
