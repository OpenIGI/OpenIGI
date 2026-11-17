using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Configuration;

namespace OpenIGI.Core.Titles;

/// <summary>One mission of a title's campaign, by its id: its place in the campaign and its name as the menus show it.</summary>
/// <param name="Id">The mission's id, which a server lists its map by.</param>
/// <param name="Number">Its place in the campaign, from 1.</param>
/// <param name="Name">Its name (Trainyard), in the language the game's menus were last shown in.</param>
public sealed record MissionName(int Id, int Number, string Name);

/// <summary>
/// The missions' names of each title, kept beside the settings by the game for the launcher. The names come from the
/// player's own installation, read by running its mission scripts -- which only the game does -- so the game writes down
/// what it read whenever it lists the missions, and the launcher's server list names maps by it.
/// </summary>
public static class MissionNames
{
    /// <summary>Writes a title's missions down, when they differ from what is written.</summary>
    /// <param name="titleId">The title's id.</param>
    /// <param name="missions">The campaign's missions.</param>
    /// <param name="folder">Where they are kept; the settings folder unless said.</param>
    public static void Save(string titleId, IReadOnlyList<MissionName> missions, string? folder = null)
    {
        ArgumentNullException.ThrowIfNull(missions);
        if (missions.Count == 0 || Load(titleId, folder).Values.OrderBy(static m => m.Id).SequenceEqual(missions.OrderBy(static m => m.Id)))
        {
            return;
        }

        try
        {
            File.WriteAllText(PathOf(titleId, folder), JsonSerializer.Serialize(missions.ToArray(), MissionNamesJson.Default.MissionNameArray));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The launcher names the maps by their numbers meanwhile.
        }
    }

    /// <summary>Reads a title's missions, by id; none when the game has not listed them yet.</summary>
    /// <param name="titleId">The title's id.</param>
    /// <param name="folder">Where they are kept; the settings folder unless said.</param>
    /// <returns>The missions.</returns>
    public static IReadOnlyDictionary<int, MissionName> Load(string titleId, string? folder = null)
    {
        try
        {
            string path = PathOf(titleId, folder);
            MissionName[]? read = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), MissionNamesJson.Default.MissionNameArray) : null;
            return (read ?? []).Where(static m => m is { Name.Length: > 0 }).GroupBy(static m => m.Id).ToDictionary(static g => g.Key, static g => g.First());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<int, MissionName>();
        }
    }

    private static string PathOf(string titleId, string? folder) =>
        folder is null ? SettingsPaths.GetFilePath($"missions-{titleId}.json") : Path.Combine(folder, $"missions-{titleId}.json");
}

/// <summary>The missions' names as JSON, generated ahead of time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(MissionName[]))]
internal sealed partial class MissionNamesJson : JsonSerializerContext;
