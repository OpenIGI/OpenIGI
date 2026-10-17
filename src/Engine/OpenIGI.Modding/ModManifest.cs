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
