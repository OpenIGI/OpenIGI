using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.IO;

namespace OpenIGI.Core.Content;

/// <summary>
/// The mods a player chose, per game, in the order they apply: what the launcher hands the game so it loads exactly those.
/// </summary>
/// <param name="Schema">The document's schema version; see <see cref="CurrentSchema"/>.</param>
/// <param name="Titles">
/// Each game's mods keyed by title id (<c>igi1</c>, <c>igi2</c>), in the order they apply: a later mod's changes are made
/// over an earlier one's. Switched-off mods travel too, so the list is the player's whole order.
/// </param>
/// <remarks>
/// <para>
/// Mods are code built against the modding API, or content that comes with a mod's manifest; asset overlays are
/// <see cref="ContentProfile"/>'s. A game started without a profile scans its <c>mods</c> folder instead, which is how a mod's
/// author tries a build.
/// </para>
/// <para>Passed by file: the launcher writes it and points <see cref="EnvironmentVariable"/> at it.</para>
/// </remarks>
public sealed record ModProfile(int Schema, IReadOnlyDictionary<string, IReadOnlyList<ModProfileEntry>> Titles)
{
    /// <summary>The schema this build writes and reads.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The environment variable the launcher names the profile file in.</summary>
    /// <remarks>A variable rather than an option, so a build older than this contract starts and ignores it.</remarks>
    public const string EnvironmentVariable = "OPENIGI_MOD_PROFILE";

    /// <summary>Gets one game's mods, or an empty list when the profile does not mention it.</summary>
    /// <param name="titleId">The title id.</param>
    /// <returns>Its mods, in the order they apply.</returns>
    public IReadOnlyList<ModProfileEntry> For(string titleId) =>
        Titles.TryGetValue(titleId, out IReadOnlyList<ModProfileEntry>? mods) ? mods : [];

    /// <summary>Serializes a profile.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>Its JSON.</returns>
    public static string Serialize(ModProfile profile) =>
        JsonSerializer.Serialize(profile, ModProfileJsonContext.Default.ModProfile);

    /// <summary>Deserializes a profile.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="JsonException">The JSON is not a profile, or is from a newer schema.</exception>
    public static ModProfile Parse(string json)
    {
        ModProfile profile = JsonSerializer.Deserialize(json, ModProfileJsonContext.Default.ModProfile)
            ?? throw new JsonException("The mod profile is empty.");

        if (profile.Schema is < 1 or > CurrentSchema)
        {
            throw new JsonException($"Mod profile schema {profile.Schema} is not one this build reads.");
        }

        return profile with { Titles = profile.Titles ?? new Dictionary<string, IReadOnlyList<ModProfileEntry>>() };
    }

    /// <summary>Reads a profile file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="JsonException">The file is not a profile.</exception>
    public static ModProfile Read(string path) => Parse(File.ReadAllText(path));

    /// <summary>Writes a profile file, whole or not at all.</summary>
    /// <param name="path">The file.</param>
    /// <param name="profile">The profile.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static void Write(string path, ModProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        AtomicFile.WriteAllText(path, Serialize(profile));
    }
}

/// <summary>One mod in a game's order.</summary>
/// <param name="Id">The mod's id, as its manifest names it.</param>
/// <param name="Path">The folder holding the mod's <c>mod.json</c>.</param>
/// <param name="Enabled">Whether the player has it switched on.</param>
public sealed record ModProfileEntry(string Id, string Path, bool Enabled);

/// <summary>The profile's JSON shape, camelCase.</summary>
/// <remarks>Source-generated, so the trimmed and ahead-of-time compiled game needs no reflection to read it.</remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(ModProfile))]
public sealed partial class ModProfileJsonContext : JsonSerializerContext;
