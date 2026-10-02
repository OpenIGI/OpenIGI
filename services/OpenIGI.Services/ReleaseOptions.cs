namespace OpenIGI.Services;

/// <summary>Where the releases are and where players download them from (the <c>Releases</c> section).</summary>
internal sealed class ReleaseOptions
{
    /// <summary>Gets or sets the folder holding one sub-folder per release, each with its <c>release.json</c>.</summary>
    public string Root { get; set; } = "/srv/dl/releases";

    /// <summary>Gets or sets where an archive is downloaded from.</summary>
    /// <remarks>
    /// <c>{folder}</c> (the release's folder on the store), <c>{tag}</c> (its git tag), <c>{version}</c> and
    /// <c>{name}</c> are replaced per asset. The default is the download store this service indexes; once the
    /// repository is public the same files are on its GitHub releases, and
    /// <c>https://github.com/OpenIGI/OpenIGI/releases/download/{tag}/{name}</c> hands those out instead.
    /// </remarks>
    public string DownloadUrl { get; set; } = "https://dl.openigi.com/releases/{folder}/{name}";

    /// <summary>Gets or sets how often the folder is looked at for new releases.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(15);
}
