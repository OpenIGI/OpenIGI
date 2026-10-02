using System.Text.Json.Serialization;

namespace OpenIGI.Releases;

/// <summary>The update channel a release is published to.</summary>
public enum ReleaseChannel
{
    /// <summary>Released from <c>master</c>; what every player gets by default.</summary>
    [JsonStringEnumMemberName("stable")]
    Stable,

    /// <summary>Released from <c>develop</c>; offered to players who opt in, alongside every stable release.</summary>
    [JsonStringEnumMemberName("beta")]
    Beta,
}

/// <summary>What a downloadable archive contains.</summary>
public enum ReleaseComponent
{
    /// <summary>The game itself (<c>apps/OpenIGI.Desktop</c>).</summary>
    [JsonStringEnumMemberName("game")]
    Game,

    /// <summary>The launcher (<c>apps/OpenIGI.Launcher</c>), which installs, updates and starts the game.</summary>
    [JsonStringEnumMemberName("launcher")]
    Launcher,

    /// <summary>The browser build, deployed to the site rather than downloaded by players.</summary>
    [JsonStringEnumMemberName("web")]
    Web,
}

/// <summary>One release, as written beside its archives as <c>release.json</c>.</summary>
/// <param name="Schema">The manifest format; <see cref="CurrentSchema"/> for everything written now.</param>
/// <param name="Version">The release version, without a leading <c>v</c>.</param>
/// <param name="Channel">The channel it was published to.</param>
/// <param name="Published">When it was published.</param>
/// <param name="Commit">The full commit hash the release was built from.</param>
/// <param name="Notes">The player-facing changes, in display order.</param>
/// <param name="Assets">The archives of the release.</param>
public sealed record ReleaseManifest(
    int Schema,
    string Version,
    ReleaseChannel Channel,
    DateTimeOffset Published,
    string Commit,
    IReadOnlyList<ReleaseNoteSection> Notes,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The manifest format this library writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The file name a manifest is stored under, beside the release's archives.</summary>
    public const string FileName = "release.json";

    /// <summary>Gets the parsed <see cref="Version"/>.</summary>
    /// <exception cref="FormatException">The manifest's version is not a semantic version.</exception>
    public ReleaseVersion ParsedVersion => ReleaseVersion.Parse(Version);

    /// <summary>Finds the download of one component for one runtime.</summary>
    /// <param name="component">The component.</param>
    /// <param name="runtime">The runtime identifier, such as <c>win-x64</c>.</param>
    /// <param name="selfContained">
    /// The build carrying its own .NET runtime (the default), or the smaller one that needs .NET installed.
    /// </param>
    /// <returns>The asset, or <see langword="null"/> when the release has none for that runtime and flavour.</returns>
    public ReleaseAsset? Find(ReleaseComponent component, string runtime, bool selfContained = true) =>
        Assets.FirstOrDefault(asset => asset.Component == component
            && asset.SelfContained == selfContained
            && string.Equals(asset.Runtime, runtime, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A group of release notes, such as "New" or "Fixes".</summary>
/// <param name="Title">The heading.</param>
/// <param name="Entries">The notes under it, oldest change first.</param>
public sealed record ReleaseNoteSection(string Title, IReadOnlyList<ReleaseNote> Entries);

/// <summary>One change, from one commit.</summary>
/// <param name="Scope">The area it touched (the commit's scope, such as <c>render</c>), when it names one.</param>
/// <param name="Text">The change, as a sentence.</param>
public sealed record ReleaseNote(string? Scope, string Text);

/// <summary>One download of a release: an executable, or an archive of a macOS bundle.</summary>
/// <param name="Name">The file name, unique within the release.</param>
/// <param name="Component">What it contains.</param>
/// <param name="Runtime">The runtime identifier it is built for, such as <c>win-x64</c> or <c>browser-wasm</c>.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Sha256">The lowercase hex SHA-256 of the file.</param>
/// <param name="Url">
/// Where to download it. Absent in the manifest written at release time (the file sits beside it); filled
/// in by the update service with the address the player should use.
/// </param>
/// <param name="SelfContained">
/// Whether it carries its own .NET runtime. The other flavour is the same program for a machine that has
/// the .NET runtime installed, and a fraction of the size.
/// </param>
public sealed record ReleaseAsset(
    string Name,
    ReleaseComponent Component,
    string Runtime,
    long Size,
    string Sha256,
    string? Url = null,
    bool SelfContained = true);

/// <summary>What the update service answers for one channel.</summary>
/// <param name="Channel">The channel asked for.</param>
/// <param name="Latest">The newest release on the channel, or <see langword="null"/> before the first.</param>
/// <param name="Releases">
/// Every release newer than the version the caller said it has, newest first, so a player several
/// releases behind sees every change they are about to receive; just <see cref="Latest"/> when the
/// caller gave no version.
/// </param>
/// <param name="Launcher">
/// The newest release, on any channel, that has a launcher. There is one launcher for everyone: the channel
/// a player follows decides which game builds they get, not which launcher.
/// </param>
public sealed record ChannelFeed(
    ReleaseChannel Channel,
    ReleaseManifest? Latest,
    IReadOnlyList<ReleaseManifest> Releases,
    ReleaseManifest? Launcher = null);
