using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Core.Content;

/// <summary>A mod as a save records it: the one that changes what happens in play, by its id and version.</summary>
/// <param name="Id">The mod's id.</param>
/// <param name="Version">The mod's version.</param>
public sealed record ModReference(string Id, string Version)
{
    /// <summary>Gets the mods a save was made with that are not being played now, at the version it was made with.</summary>
    /// <param name="saved">The mods the save records, or <see langword="null"/> for a save that records none.</param>
    /// <param name="playing">The mods being played.</param>
    /// <returns>The missing ones, in the save's order; none for a save that records none.</returns>
    public static IReadOnlyList<ModReference> Missing(IReadOnlyList<ModReference>? saved, IReadOnlyList<ModReference> playing)
    {
        ArgumentNullException.ThrowIfNull(playing);
        return saved is null
            ? []
            : [.. saved.Where(mod => !playing.Any(p => string.Equals(p.Id, mod.Id, StringComparison.OrdinalIgnoreCase) && p.Version == mod.Version))];
    }

    /// <summary>Writes a list of mods beside a save, or removes the list when there are none.</summary>
    /// <param name="path">The list's file.</param>
    /// <param name="mods">The mods.</param>
    public static void WriteList(string path, IReadOnlyList<ModReference> mods)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(mods);
        if (mods.Count == 0)
        {
            File.Delete(path);
            return;
        }

        File.WriteAllText(path, JsonSerializer.Serialize([.. mods], ModReferenceJson.Default.ModReferenceArray));
    }

    /// <summary>Reads the list of mods beside a save.</summary>
    /// <param name="path">The list's file.</param>
    /// <returns>The mods, or <see langword="null"/> when there is no list or it cannot be read.</returns>
    public static IReadOnlyList<ModReference>? ReadList(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), ModReferenceJson.Default.ModReferenceArray) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Id} {Version}";
}

/// <summary>The list's JSON shape, generated so that reading one needs no reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ModReference[]))]
internal sealed partial class ModReferenceJson : JsonSerializerContext;
