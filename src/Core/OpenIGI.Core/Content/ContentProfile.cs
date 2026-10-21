using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.IO;

namespace OpenIGI.Core.Content;

/// <summary>
/// The content a player chose, per game, in the order they chose it: what the launcher hands the game so it
/// knows which asset overlays to stack and in which order.
/// </summary>
/// <param name="Schema">The document's schema version; see <see cref="CurrentSchema"/>.</param>
/// <param name="Titles">
/// Each game's layers keyed by title id (<c>igi1</c>, <c>igi2</c>), highest priority first. The whole list
/// travels -- every game, disabled layers included -- so a title switch inside the game picks up the other
/// game's list and the console can switch a layer on without a relaunch.
/// </param>
/// <remarks>
/// <para>
/// <b>Content only.</b> Content is asset overlays: folders that mirror the game's paths and replace its
/// files. Mods -- code packages built against the game's plugin interfaces -- are a different thing with a
/// different contract, folder (<c>&lt;Library&gt;/mods/&lt;id&gt;</c>) and loader, and never pass through this
/// profile but through <see cref="ModProfile"/>. See "Content packs and mods" in <c>CONTRIBUTING.md</c>.
/// </para>
/// <para>
/// Passed by file: the launcher writes it and points <see cref="EnvironmentVariable"/> (or the
/// <c>-contentprofile</c> option) at it.
/// </para>
/// </remarks>
public sealed record ContentProfile(int Schema, IReadOnlyDictionary<string, IReadOnlyList<ContentLayerSpec>> Titles)
{
    /// <summary>The schema this build writes and reads.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The environment variable the launcher names the profile file in.</summary>
    /// <remarks>
    /// A variable rather than only an option, because the launcher may start a build older than this
    /// contract: an unknown variable is ignored, where an unknown option stops the game from starting.
    /// </remarks>
    public const string EnvironmentVariable = "OPENIGI_CONTENT_PROFILE";

    /// <summary>Gets the layers for one game, or an empty list when the profile does not mention it.</summary>
    /// <param name="titleId">The title id.</param>
    /// <returns>Its layers, highest priority first.</returns>
    public IReadOnlyList<ContentLayerSpec> For(string titleId) =>
        Titles.TryGetValue(titleId, out IReadOnlyList<ContentLayerSpec>? layers) ? layers : [];

    /// <summary>Serializes a profile.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>Its JSON.</returns>
    public static string Serialize(ContentProfile profile) =>
        JsonSerializer.Serialize(profile, ContentProfileJsonContext.Default.ContentProfile);

    /// <summary>Deserializes a profile.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="JsonException">The JSON is not a profile, or is from a newer schema.</exception>
    public static ContentProfile Parse(string json)
    {
        ContentProfile profile = JsonSerializer.Deserialize(json, ContentProfileJsonContext.Default.ContentProfile)
            ?? throw new JsonException("The content profile is empty.");

        if (profile.Schema is < 1 or > CurrentSchema)
        {
            throw new JsonException($"Content profile schema {profile.Schema} is not one this build reads.");
        }

        return profile with { Titles = profile.Titles ?? new Dictionary<string, IReadOnlyList<ContentLayerSpec>>() };
    }

    /// <summary>Reads a profile file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="JsonException">The file is not a profile.</exception>
    public static ContentProfile Read(string path) => Parse(File.ReadAllText(path));

    /// <summary>Writes a profile file, whole or not at all.</summary>
    /// <param name="path">The file.</param>
    /// <param name="profile">The profile.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static void Write(string path, ContentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // A game starting at the same moment never reads half.
        AtomicFile.WriteAllText(path, Serialize(profile));
    }
}

/// <summary>One entry of a game's content load order.</summary>
/// <param name="Id">A stable identity: the pack id, or <see cref="ContentLayerSpec.LocalId"/>.</param>
/// <param name="Name">What the player sees it called.</param>
/// <param name="Kind">What sort of folder it is.</param>
/// <param name="Path">The folder, or <see langword="null"/> for <see cref="ContentLayerKind.Local"/>.</param>
/// <param name="Enabled">Whether the player has it switched on.</param>
public sealed record ContentLayerSpec(string Id, string Name, ContentLayerKind Kind, string? Path, bool Enabled)
{
    /// <summary>The id of the game folder's own overrides layer.</summary>
    public const string LocalId = "local";

    /// <summary>The name the game folder's own overrides layer goes by.</summary>
    public const string LocalName = "Local overrides (game folder)";

    /// <summary>Gets the game folder's own overrides layer.</summary>
    /// <param name="enabled">Whether it is switched on.</param>
    /// <returns>The entry.</returns>
    public static ContentLayerSpec Local(bool enabled = true) =>
        new(LocalId, LocalName, ContentLayerKind.Local, null, enabled);
}

/// <summary>What sort of folder a content layer is.</summary>
public enum ContentLayerKind
{
    /// <summary>An installed content pack; <see cref="ContentLayerSpec.Path"/> is its files folder.</summary>
    [JsonStringEnumMemberName("pack")]
    Pack,

    /// <summary>The game folder's own <c>overrides</c> directory, wherever the game finds its data.</summary>
    [JsonStringEnumMemberName("local")]
    Local,

    /// <summary>Any other folder, named directly.</summary>
    [JsonStringEnumMemberName("directory")]
    Directory,
}

/// <summary>The profile's JSON shape: camelCase, kinds as lowercase words.</summary>
/// <remarks>Source-generated, so the trimmed and ahead-of-time compiled game needs no reflection to read it.</remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ContentProfile))]
public sealed partial class ContentProfileJsonContext : JsonSerializerContext;
