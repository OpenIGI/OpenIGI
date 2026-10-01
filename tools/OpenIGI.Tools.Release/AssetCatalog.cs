using System.Security.Cryptography;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Finds a release's archives in a folder and describes each one.</summary>
/// <remarks>
/// Archives are named <c>openigi-&lt;component&gt;-&lt;version&gt;-&lt;runtime&gt;.&lt;zip|tar.gz&gt;</c> by
/// <c>tools/release/package.sh</c>, which is the only thing that names them, so the name alone says what
/// an archive holds and which machine it is for.
/// </remarks>
public static class AssetCatalog
{
    private static readonly string[] Extensions = [".tar.gz", ".zip"];

    /// <summary>The file name the packaging script gives an archive.</summary>
    /// <param name="component">What it contains.</param>
    /// <param name="version">The release version.</param>
    /// <param name="runtime">The runtime identifier.</param>
    /// <param name="extension">The archive extension, with its dot.</param>
    /// <returns>The file name.</returns>
    public static string FileName(ReleaseComponent component, string version, string runtime, string extension) =>
        $"openigi-{component.ToString().ToLowerInvariant()}-{version}-{runtime}{extension}";

    /// <summary>Describes every archive of one release in a folder.</summary>
    /// <param name="directory">The folder holding the archives.</param>
    /// <param name="version">The release version the archives must be named for.</param>
    /// <returns>The assets, ordered by component then runtime.</returns>
    /// <exception cref="InvalidDataException">A file in the folder is not an archive of this release.</exception>
    public static IReadOnlyList<ReleaseAsset> Scan(string directory, string version)
    {
        List<ReleaseAsset> assets = [];
        foreach (string path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (name is ReleaseManifest.FileName or "SHA256SUMS" or "CHANGELOG.md")
            {
                continue;
            }

            if (!TryParse(name, version, out ReleaseComponent component, out string runtime))
            {
                throw new InvalidDataException(
                    $"'{name}' is not named like an archive of {version} (openigi-<component>-{version}-<runtime>.zip|.tar.gz).");
            }

            assets.Add(new ReleaseAsset(name, component, runtime, new FileInfo(path).Length, Sha256(path)));
        }

        return [.. assets.OrderBy(static a => a.Component).ThenBy(static a => a.Runtime, StringComparer.Ordinal)];
    }

    /// <summary>Reads the component and runtime back out of an archive's name.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="version">The release version it must be named for.</param>
    /// <param name="component">The component, when the name parses.</param>
    /// <param name="runtime">The runtime identifier, when the name parses.</param>
    /// <returns>Whether the name is an archive of this release.</returns>
    public static bool TryParse(string name, string version, out ReleaseComponent component, out string runtime)
    {
        component = default;
        runtime = string.Empty;

        string? extension = Extensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.Ordinal));
        if (extension is null)
        {
            return false;
        }

        foreach (ReleaseComponent candidate in Enum.GetValues<ReleaseComponent>())
        {
            string prefix = $"openigi-{candidate.ToString().ToLowerInvariant()}-{version}-";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                runtime = name[prefix.Length..^extension.Length];
                component = candidate;
                return runtime.Length > 0;
            }
        }

        return false;
    }

    /// <summary>Hashes a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its lowercase hex SHA-256.</returns>
    public static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
