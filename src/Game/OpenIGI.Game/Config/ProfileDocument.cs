using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Config;

/// <summary>One key binding as a profile document carries it.</summary>
/// <param name="Action">The action.</param>
/// <param name="Device">The input device.</param>
/// <param name="Key">The key or button.</param>
public sealed record ProfileBinding(string Action, string Device, string Key);

/// <summary>The best a player has done in one mission of one game, as a profile document carries it.</summary>
/// <param name="Title">The game, by title id.</param>
/// <param name="Mission">The mission.</param>
/// <param name="Counts">The record's counts.</param>
/// <param name="Figures">The record's figures.</param>
public sealed record ProfileMissionRecord(string Title, int Mission, IReadOnlyList<int> Counts, IReadOnlyList<float> Figures);

/// <summary>
/// What of a player's profile follows them between their computers and the browser, through their account: how far they
/// are in each game, their difficulty, and how they play -- the mouse and their keys. What belongs to one computer (its
/// display, its sound, its graphics) stays there, and so does the profile's name, which its saves are filed under; the
/// player is known online by their Discord name.
/// </summary>
/// <param name="Schema">The document's version.</param>
/// <param name="Missions">The mission reached in each game, by title id.</param>
/// <param name="Difficulty">The campaign's difficulty.</param>
/// <param name="InvertMouse">Whether the mouse's vertical axis is inverted.</param>
/// <param name="MouseSensitivity">The mouse's sensitivity, 0 to 1.</param>
/// <param name="Bindings">The keys, as the profile lists them.</param>
/// <param name="MissionRecords">
/// The best done in each mission, by game; <see langword="null"/> in a document an older build wrote, which knew none -- and which
/// passes over the list when it reads a newer one.
/// </param>
/// <param name="Finales">
/// The last mission completed in each game whose campaign is finished, by title id; <see langword="null"/> in a document an older
/// build wrote.
/// </param>
public sealed record ProfileDocument(
    int Schema,
    IReadOnlyDictionary<string, int> Missions,
    int Difficulty,
    bool InvertMouse,
    float MouseSensitivity,
    IReadOnlyList<ProfileBinding> Bindings,
    IReadOnlyList<ProfileMissionRecord>? MissionRecords = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, int>? Finales = null)
{
    /// <summary>The version this build writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Takes what follows the player from a profile.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The document.</returns>
    public static ProfileDocument From(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var missions = new SortedDictionary<string, int>(StringComparer.Ordinal) { [PlayerProfile.RetailTitle] = profile.ActiveMission };
        foreach ((string title, int mission) in profile.TitleMissions)
        {
            missions[title] = mission;
        }

        return new ProfileDocument(
            CurrentSchema,
            missions,
            profile.Difficulty,
            profile.InvertMouse,
            profile.MouseSensitivity,
            [.. profile.Bindings.Select(static b => new ProfileBinding(b.Action, b.Device, b.Key))],
            [.. profile.MissionRecords.Select(static r => new ProfileMissionRecord(r.TitleId, r.MissionId, r.Record.Counts, r.Record.Figures))],
            // None kept is left out, so a profile that has finished nothing reads as it did before finales were kept.
            profile.Finales.Count == 0 ? null : new SortedDictionary<string, int>(profile.Finales.ToDictionary(), StringComparer.Ordinal));
    }

    /// <summary>
    /// Merges what this computer has with what the account has: in each game the furthest mission either reached (missions
    /// are numbered in campaign order) and the furthest finale either finished, in each mission the better best (<see cref="MissionScoring.Prefer"/>), and everything
    /// else from the side that changed last.
    /// </summary>
    /// <param name="local">This computer's.</param>
    /// <param name="remote">The account's.</param>
    /// <param name="localIsNewer">Whether this computer's settings changed since they were last in step.</param>
    /// <returns>The merged document.</returns>
    public static ProfileDocument Merge(ProfileDocument local, ProfileDocument remote, bool localIsNewer)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        var missions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string title, int mission) in local.Missions.Concat(remote.Missions))
        {
            missions[title] = Math.Max(missions.GetValueOrDefault(title), mission);
        }

        ProfileDocument settings = localIsNewer ? local : remote;
        return settings with { Schema = CurrentSchema, Missions = missions, MissionRecords = MergeRecords(local, remote), Finales = Furthest(local.Finales, remote.Finales) is { Count: > 0 } finales ? finales : null };
    }

    // In each game, the furthest mission either side finished the campaign on.
    private static SortedDictionary<string, int> Furthest(IReadOnlyDictionary<string, int>? local, IReadOnlyDictionary<string, int>? remote)
    {
        var furthest = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string title, int mission) in (local ?? new Dictionary<string, int>()).Concat(remote ?? new Dictionary<string, int>()))
        {
            furthest[title] = Math.Max(furthest.GetValueOrDefault(title), mission);
        }

        return furthest;
    }

    // Every mission either side keeps a record of; where both do, the better.
    private static List<ProfileMissionRecord> MergeRecords(ProfileDocument local, ProfileDocument remote)
    {
        var merged = new Dictionary<(string Title, int Mission), TitleMissionRecord>();
        foreach (ProfileMissionRecord record in remote.MissionRecords ?? [])
        {
            merged[(record.Title, record.Mission)] = new TitleMissionRecord(record.Counts, record.Figures);
        }

        foreach (ProfileMissionRecord record in local.MissionRecords ?? [])
        {
            var mine = new TitleMissionRecord(record.Counts, record.Figures);
            merged[(record.Title, record.Mission)] = merged.TryGetValue((record.Title, record.Mission), out TitleMissionRecord? theirs)
                ? MissionScoring.Prefer(mine, theirs)
                : mine;
        }

        return
        [
            .. merged
                .OrderBy(static entry => entry.Key.Title, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Key.Mission)
                .Select(static entry => new ProfileMissionRecord(entry.Key.Title, entry.Key.Mission, entry.Value.Counts, entry.Value.Figures)),
        ];
    }

    /// <summary>Puts the document into a profile.</summary>
    /// <param name="profile">The profile.</param>
    public void ApplyTo(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        foreach ((string title, int mission) in Missions)
        {
            if (mission > 0)
            {
                profile.SetMission(title, mission);
            }
        }

        foreach ((string title, int mission) in Finales ?? new Dictionary<string, int>())
        {
            if (title.Length > 0)
            {
                profile.SetFinale(title, mission);
            }
        }

        foreach (ProfileMissionRecord record in MissionRecords ?? [])
        {
            if (record is { Title.Length: > 0, Counts: not null, Figures: not null })
            {
                profile.SetMissionRecord(record.Title, record.Mission, new TitleMissionRecord(record.Counts, record.Figures));
            }
        }

        profile.Difficulty = Difficulty;
        profile.InvertMouse = InvertMouse;
        profile.MouseSensitivity = Math.Clamp(MouseSensitivity, 0f, 1f);
        if (Bindings.Count > 0)
        {
            profile.Bindings.Clear();
            profile.Bindings.AddRange(Bindings.Select(static b => new InputBinding(b.Action, b.Device, b.Key)));
            profile.RestoreMissingDefaultBindings();
        }
    }

    /// <summary>Writes the document as JSON.</summary>
    /// <returns>The JSON.</returns>
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, ProfileDocumentJson.Default.ProfileDocument);

    /// <summary>Reads a document, or <see langword="null"/> for one this build can't read.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The document.</returns>
    public static ProfileDocument? FromJson(JsonElement json)
    {
        try
        {
            ProfileDocument? document = json.Deserialize(ProfileDocumentJson.Default.ProfileDocument);
            return document is { Schema: > 0 and <= CurrentSchema } && document.Missions is not null && document.Bindings is not null ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Gets a fingerprint of the document: what tells whether a profile changed since it was last in step.</summary>
    /// <returns>Sixteen hex digits.</returns>
    public string Fingerprint() =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this, ProfileDocumentJson.Default.ProfileDocument)))[..16];
}

/// <summary>The JSON of <see cref="ProfileDocument"/>, generated ahead of time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProfileDocument))]
internal sealed partial class ProfileDocumentJson : JsonSerializerContext;
