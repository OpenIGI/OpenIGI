using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Titles;

namespace OpenIGI.Modding;

/// <summary>What a mod says about itself in its <c>mod.json</c>: who it is, which games it is for, and what it changes.</summary>
/// <param name="Id">A stable machine identifier, unique among the mods a player has installed.</param>
/// <param name="Name">The name shown to the player.</param>
/// <param name="Version">The mod's own version.</param>
/// <param name="ApiVersion">The modding API the mod was written against; see <see cref="ModApi.Version"/>.</param>
/// <param name="Titles">The ids of the games the mod is for; empty for every game.</param>
/// <param name="Assembly">The mod's code, a file beside <c>mod.json</c>; none for a mod made of content alone.</param>
/// <param name="AffectsSimulation">
/// Whether the mod changes what happens in play rather than only how it looks or sounds. A save or a replay records the
/// mods that do.
/// </param>
public sealed record ModManifest(
    string Id,
    string Name,
    string Version,
    int ApiVersion,
    IReadOnlyList<string> Titles,
    string? Assembly = null,
    bool AffectsSimulation = true)
{
    /// <summary>The manifest's file name, in the mod's directory.</summary>
    public const string FileName = "mod.json";

    /// <summary>Gets who made the mod, as the launcher credits them.</summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>Gets a line saying what the mod does, or <see langword="null"/>.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets where the mod's page or source is, or <see langword="null"/>.</summary>
    public string? Homepage { get; init; }

    /// <summary>Gets the mod's licence, as an SPDX expression, or <see langword="null"/>.</summary>
    public string? License { get; init; }

    /// <summary>
    /// Gets the ids of the other games whose installed data the mod reads besides the one it is for; it is left out when one
    /// of them is not installed.
    /// </summary>
    public IReadOnlyList<string> RequiresTitleData { get; init; } = [];

    /// <summary>
    /// Gets the mods this one needs switched on with it, by id, each with the oldest version it works with (<c>1.2</c> or
    /// <c>&gt;=1.2</c>; <c>*</c> for any); see <see cref="ModVersion.Satisfies"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Dependencies { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Gets the ids of mods this one cannot be played with; the later of the two in the order is left out.</summary>
    public IReadOnlyList<string> Conflicts { get; init; } = [];

    /// <summary>Gets the oldest game build the mod works with, or <see langword="null"/>; the launcher checks it.</summary>
    public string? MinGameVersion { get; init; }

    /// <summary>Gets whether the mod has code, which runs with the game's own rights.</summary>
    public bool RunsCode => Assembly is { Length: > 0 };

    /// <summary>Reads a manifest.</summary>
    /// <param name="json">The manifest's text.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="ModLoadException">The text is not a manifest, or it leaves out what every mod must say.</exception>
    public static ModManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ModManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(json, ModManifestJson.Default.ModManifest);
        }
        catch (JsonException ex)
        {
            throw new ModLoadException($"The manifest is not valid: {ex.Message}", ex);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new ModLoadException("The manifest names no id.");
        }

        return manifest with
        {
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name,
            Version = manifest.Version ?? "0",
            Titles = manifest.Titles ?? [],
            Authors = manifest.Authors ?? [],
            RequiresTitleData = manifest.RequiresTitleData ?? [],
            Dependencies = manifest.Dependencies ?? ReadOnlyDictionary<string, string>.Empty,
            Conflicts = manifest.Conflicts ?? [],
        };
    }

    /// <summary>Gets whether the mod is for a game.</summary>
    /// <param name="title">The game.</param>
    /// <returns>Whether it is.</returns>
    public bool AppliesTo(GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return Titles.Count == 0 || Titles.Any(id => string.Equals(id, title.Id, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The manifest's JSON shape, generated so that reading one needs no reflection.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ModManifest))]
internal sealed partial class ModManifestJson : JsonSerializerContext;
