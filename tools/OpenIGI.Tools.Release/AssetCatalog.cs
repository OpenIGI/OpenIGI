using System.Security.Cryptography;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Finds a release's downloads in a folder and describes each one.</summary>
/// <remarks>
/// Downloads are named <c>openigi-&lt;component&gt;-&lt;version&gt;-&lt;runtime&gt;[-dotnet][.exe|.tar.gz]</c> by
/// <c>tools/release/package.sh</c>, which is the only thing that names them, so the name alone says what a
/// file holds, which machine it is for, and whether it carries its own .NET runtime: a Windows or Linux
/// build is the executable itself (no extension on Linux), a macOS build a tarball of its application
/// bundle, and <c>-dotnet</c> marks the smaller flavour that needs the .NET runtime installed.
/// </remarks>
public static class AssetCatalog
{
    private const string DotnetSuffix = "-dotnet";

    private static readonly string[] Extensions = [".tar.gz", ".exe"];

    private static readonly string[] Runtimes = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64", "osx-x64"];

    /// <summary>The file name the packaging script gives a download.</summary>
    /// <param name="component">What it contains.</param>
    /// <param name="version">The release version.</param>
    /// <param name="runtime">The runtime identifier.</param>
    /// <param name="selfContained">Whether it carries its own .NET runtime.</param>
    /// <returns>The file name.</returns>
    public static string FileName(ReleaseComponent component, string version, string runtime, bool selfContained = true)
    {
        string extension = runtime.StartsWith("win-", StringComparison.Ordinal) ? ".exe"
            : runtime.StartsWith("osx-", StringComparison.Ordinal) ? ".tar.gz"
            : string.Empty;
        return $"openigi-{component.ToString().ToLowerInvariant()}-{version}-{runtime}{(selfContained ? string.Empty : DotnetSuffix)}{extension}";
    }

    /// <summary>Describes every download of one release in a folder.</summary>
    /// <param name="directory">The folder holding the downloads.</param>
    /// <param name="version">The release version the files must be named for.</param>
    /// <returns>The assets, ordered by component, runtime, then self-contained first.</returns>
    /// <exception cref="InvalidDataException">A file in the folder is not a download of this release.</exception>
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

            if (!TryParse(name, version, out ReleaseComponent component, out string runtime, out bool selfContained))
            {
                throw new InvalidDataException(
                    $"'{name}' is not named like a download of {version} (openigi-<component>-{version}-<runtime>[-dotnet][.exe|.tar.gz]).");
            }

            assets.Add(new ReleaseAsset(name, component, runtime, new FileInfo(path).Length, Sha256(path), SelfContained: selfContained));
        }

        return [.. assets
            .OrderBy(static a => a.Component)
            .ThenBy(static a => a.Runtime, StringComparer.Ordinal)
            .ThenByDescending(static a => a.SelfContained)];
    }

    /// <summary>Reads the component, runtime and flavour back out of a download's name.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="version">The release version it must be named for.</param>
    /// <param name="component">The component, when the name parses.</param>
    /// <param name="runtime">The runtime identifier, when the name parses.</param>
    /// <param name="selfContained">Whether it carries its own .NET runtime, when the name parses.</param>
    /// <returns>Whether the name is a download of this release.</returns>
    public static bool TryParse(string name, string version, out ReleaseComponent component, out string runtime, out bool selfContained)
    {
        component = default;
        runtime = string.Empty;
        selfContained = true;

        foreach (ReleaseComponent candidate in Enum.GetValues<ReleaseComponent>())
        {
            string prefix = $"openigi-{candidate.ToString().ToLowerInvariant()}-{version}-";
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string rest = name[prefix.Length..];
            string extension = Extensions.FirstOrDefault(e => rest.EndsWith(e, StringComparison.Ordinal)) ?? string.Empty;
            rest = rest[..^extension.Length];
            bool dotnet = rest.EndsWith(DotnetSuffix, StringComparison.Ordinal);
            if (dotnet)
            {
                rest = rest[..^DotnetSuffix.Length];
            }

            // The extension must be the one the runtime is packed with, so a stray file cannot pass for a build.
            if (!Runtimes.Contains(rest, StringComparer.Ordinal)
                || FileName(candidate, version, rest, !dotnet) != name)
            {
                return false;
            }

            component = candidate;
            runtime = rest;
            selfContained = !dotnet;
            return true;
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
