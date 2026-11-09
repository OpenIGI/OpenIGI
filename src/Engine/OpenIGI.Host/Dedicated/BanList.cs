using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Host.Dedicated;

/// <summary>A ban: a player's key kept out, the name it went by, why, and until when.</summary>
/// <param name="PlayerId">The verified key's id.</param>
/// <param name="Name">The name it played under when banned.</param>
/// <param name="Reason">Why.</param>
/// <param name="Until">When it ends, or <see langword="null"/> for never.</param>
public sealed record Ban(string PlayerId, string Name, string Reason, DateTimeOffset? Until);

/// <summary>
/// A dedicated server's bans, kept in a JSON file beside it (<c>bans.json</c>) so they outlast the process: keyed by the
/// player's verified key, which a new name does not change. A ban run out is forgotten as it is next looked at.
/// </summary>
/// <param name="path">The file, or <see langword="null"/> for bans kept in memory only.</param>
/// <param name="clock">The time now; the system's when omitted.</param>
public sealed class BanList(string? path, Func<DateTimeOffset>? clock = null)
{
    private readonly Dictionary<string, Ban> _bans = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Gets the bans in force.</summary>
    public IReadOnlyList<Ban> Bans
    {
        get
        {
            Expire();
            return [.. _bans.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>Reads the file, if there is one.</summary>
    /// <returns>The problem reading it, or <see langword="null"/>.</returns>
    public string? Load()
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            foreach (Ban ban in JsonSerializer.Deserialize(File.ReadAllText(path), DedicatedJsonContext.Default.BanArray) ?? [])
            {
                if (!string.IsNullOrEmpty(ban.PlayerId))
                {
                    _bans[ban.PlayerId] = ban;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return $"{path} could not be read: {ex.Message}";
        }
    }

    /// <summary>Gets why a key is banned, as it is told, or <see langword="null"/> when it is not.</summary>
    /// <param name="playerId">The key's id.</param>
    /// <returns>The reason.</returns>
    public string? Check(string playerId)
    {
        Expire();
        if (!_bans.TryGetValue(playerId, out Ban? ban))
        {
            return null;
        }

        string why = ban.Reason.Length > 0 ? ban.Reason : "no reason given";
        return ban.Until is { } until
            ? $"banned from this server until {until:yyyy-MM-dd HH:mm} UTC: {why}"
            : $"banned from this server: {why}";
    }

    /// <summary>Bans a key.</summary>
    /// <param name="playerId">The key's id.</param>
    /// <param name="name">The name it plays under.</param>
    /// <param name="minutes">For how long, or 0 for ever.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The ban.</returns>
    public Ban Add(string playerId, string name, double minutes, string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(playerId);
        Ban ban = new(playerId, name ?? string.Empty, reason ?? string.Empty, minutes > 0 ? _now().AddMinutes(minutes) : null);
        _bans[playerId] = ban;
        Save();
        return ban;
    }

    /// <summary>Lifts a ban, by key id or by the name it was banned under.</summary>
    /// <param name="who">The id, or the name.</param>
    /// <returns>The ban lifted, or <see langword="null"/>.</returns>
    public Ban? Remove(string who)
    {
        Ban? ban = _bans.GetValueOrDefault(who) ?? _bans.Values.FirstOrDefault(b => string.Equals(b.Name, who, StringComparison.OrdinalIgnoreCase));
        if (ban is not null)
        {
            _bans.Remove(ban.PlayerId);
            Save();
        }

        return ban;
    }

    private void Expire()
    {
        DateTimeOffset now = _now();
        string[] done = [.. _bans.Values.Where(b => b.Until is { } until && until <= now).Select(b => b.PlayerId)];
        foreach (string id in done)
        {
            _bans.Remove(id);
        }

        if (done.Length > 0)
        {
            Save();
        }
    }

    private void Save()
    {
        if (path is null)
        {
            return;
        }

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_bans.Values.ToArray(), DedicatedJsonContext.Default.BanArray));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>The dedicated server's files and messages, serialised without reflection.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Ban[]))]
[JsonSerializable(typeof(HeartbeatBody))]
[JsonSerializable(typeof(ShutdownBody))]
internal sealed partial class DedicatedJsonContext : JsonSerializerContext;
