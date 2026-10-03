using System.Text.Json;
using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Host.Configuration;

namespace OpenIGI.Host.Content;

/// <summary>Works out the stack of content layers one title runs with.</summary>
/// <remarks>
/// <para>
/// Highest priority first:
/// </para>
/// <list type="number">
/// <item><description>every <c>-content</c> folder, in the order named (development);</description></item>
/// <item><description>the folder in <c>OPENIGI_OVERRIDES</c>, if set and there is NO profile (the launcher's
/// old hand-off, kept so scripts and shortcuts that set it still work);</description></item>
/// <item><description>the content profile's list for this title, which is the player's load order and
/// holds the game folder's own <c>overrides</c> as one entry among the packs;</description></item>
/// </list>
/// <para>
/// With no profile, or a profile that does not mention this title, the third part is the game folder's
/// own overrides alone -- the behaviour from before there were packs.
/// </para>
/// <para>
/// The legacy folder is ignored whenever a profile is in force. The launcher sets both on purpose, because
/// it may start a build from before profiles, which reads only <c>OPENIGI_OVERRIDES</c>; a build that reads
/// the profile must not also stack that folder, or the pack the old build was given would sit above the
/// player's whole order, and win even when it is switched off.
/// </para>
/// <para>
/// Re-run on every title switch, because each game has its own list and its own data root.
/// </para>
/// </remarks>
public static class ContentLayers
{
    /// <summary>The variable the launcher used to name a single overrides folder in.</summary>
    public const string LegacyVariable = "OPENIGI_OVERRIDES";

    private const string LogCategory = "Overrides";

    /// <summary>Resolves the layers for a title.</summary>
    /// <param name="titleId">The title id.</param>
    /// <param name="dataRoot">The title's data root, where its own <c>overrides</c> folder lives.</param>
    /// <param name="profilePath">The content profile file, or <see langword="null"/> for none.</param>
    /// <param name="developmentFolders">The <c>-content</c> folders, highest priority first.</param>
    /// <param name="legacyFolder">The <c>OPENIGI_OVERRIDES</c> folder, or <see langword="null"/>.</param>
    /// <param name="logger">Where to report a profile that could not be read.</param>
    /// <returns>The layers, highest priority first.</returns>
    public static IReadOnlyList<OverrideLayer> Resolve(
        string titleId,
        string? dataRoot,
        string? profilePath,
        IReadOnlyList<string> developmentFolders,
        string? legacyFolder,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(titleId);
        ArgumentNullException.ThrowIfNull(developmentFolders);

        logger ??= NullLogger.Instance;
        List<OverrideLayer> layers = [];

        foreach (string folder in developmentFolders)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
            layers.Add(new OverrideLayer(Unique(layers, "dev-" + name), $"{name} (-content)", folder));
        }

        bool profiled = !string.IsNullOrWhiteSpace(profilePath);
        if (!string.IsNullOrWhiteSpace(legacyFolder) && !profiled)
        {
            layers.Add(new OverrideLayer(Unique(layers, "legacy"), $"Legacy ({LegacyVariable})", legacyFolder));
        }

        IReadOnlyList<ContentLayerSpec> chosen = ReadProfile(titleId, profilePath, logger);
        if (chosen.Count == 0)
        {
            chosen = [ContentLayerSpec.Local()];
        }

        foreach (ContentLayerSpec spec in chosen)
        {
            string? root = spec.Kind == ContentLayerKind.Local
                ? dataRoot is null ? null : Path.Combine(dataRoot, ResourceOverrides.DirectoryName)
                : spec.Path;

            layers.Add(new OverrideLayer(Unique(layers, spec.Id), spec.Name, root, spec.Enabled));
        }

        return layers;
    }

    /// <summary>Resolves the layers for a title from the process's own options and environment.</summary>
    /// <param name="titleId">The title id.</param>
    /// <param name="dataRoot">The title's data root.</param>
    /// <param name="options">The parsed command line.</param>
    /// <param name="logger">Where to report a profile that could not be read.</param>
    /// <returns>The layers, highest priority first.</returns>
    public static IReadOnlyList<OverrideLayer> Resolve(
        string titleId,
        string? dataRoot,
        HostOptions options,
        IOpenIgiLogger? logger = null) =>
        Resolve(
            titleId,
            dataRoot,
            options.ContentProfilePath ?? NonEmpty(Environment.GetEnvironmentVariable(ContentProfile.EnvironmentVariable)),
            options.ContentDirectories,
            NonEmpty(Environment.GetEnvironmentVariable(LegacyVariable)),
            logger);

    private static IReadOnlyList<ContentLayerSpec> ReadProfile(string titleId, string? profilePath, IOpenIgiLogger logger)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return [];
        }

        try
        {
            return ContentProfile.Read(profilePath).For(titleId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // Not fatal: the game is playable without its packs, and the reason is in the log.
            logger.Warning(LogCategory, $"Content profile '{profilePath}' could not be read, so only the game folder's overrides are used: {error.Message}");
            return [];
        }
    }

    // Two layers with one id would make "content off <id>" ambiguous; the later one gets a suffix.
    private static string Unique(List<OverrideLayer> layers, string id)
    {
        string candidate = id;
        for (int n = 2; layers.Any(layer => string.Equals(layer.Id, candidate, StringComparison.OrdinalIgnoreCase)); n++)
        {
            candidate = $"{id}-{n}";
        }

        return candidate;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
