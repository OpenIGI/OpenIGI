using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Igi1.Flow;

/// <summary>
/// What each rank asks of Project I.G.I.'s missions. <b>Not retail</b>: the original ranks none; these are the remaster's, read from
/// a JSON table (<c>ranks.igi1.json</c>, shipped inside the game) laid out as I.G.I. 2's <c>MissionScoreSettings</c> lays a level's,
/// so the two games' missions are ranked by the same rules (<see cref="MissionScoring"/>) and named by the same ladder.
/// </summary>
/// <remarks>
/// <para>
/// The table's <c>ranks</c> holds, for each figure, what Bodyguard, Secret Agent and IGI Operative ask; the times as multiples of a
/// mission's par time, which each mission sets. A mission may set any row of its own. A figure whose first value is below nought
/// is not weighed, as in I.G.I. 2.
/// </para>
/// <para>
/// A <see cref="FileName"/> in a mission's folder -- a mod's, or the game's overrides -- takes the mission's place in the table, in
/// the shape of one of its entries; what it leaves out comes from the table's own rows. A mod's mission is ranked by the one it
/// ships, and not ranked without one. A file that cannot be read is reported and passed over.
/// </para>
/// </remarks>
public sealed class Igi1MissionRanks
{
    /// <summary>The name of a mission's own requirements in its folder.</summary>
    public const string FileName = "ranks.json";

    /// <summary>The table format this build reads.</summary>
    public const int CurrentFormat = 1;

    private const string ResourceName = "OpenIGI.Game.Igi1.ranks.json";

    private static readonly Lazy<Igi1MissionRanks> ShippedTable = new(static () => Parse(ReadShipped()));

    private readonly RankRows _defaults;
    private readonly Dictionary<int, MissionEntry> _missions;

    private Igi1MissionRanks(RankRows defaults, Dictionary<int, MissionEntry> missions)
    {
        _defaults = defaults;
        _missions = missions;
    }

    /// <summary>Gets the table the game ships.</summary>
    public static Igi1MissionRanks Shipped => ShippedTable.Value;

    /// <summary>Gets the missions the table ranks.</summary>
    public IEnumerable<int> RankedMissions => _missions.Keys;

    /// <summary>Reads a table.</summary>
    /// <param name="json">The table's text.</param>
    /// <returns>The table.</returns>
    /// <exception cref="JsonException">The text is not a table this build reads.</exception>
    public static Igi1MissionRanks Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        TableFile file = JsonSerializer.Deserialize(json, Igi1MissionRanksJson.Default.TableFile)
            ?? throw new JsonException("The ranks table is empty.");
        CheckFormat(file.Format);
        RankRows defaults = file.Ranks ?? throw new JsonException("The ranks table has no 'ranks'.");
        defaults.Check(complete: true);

        var missions = new Dictionary<int, MissionEntry>();
        foreach ((string key, MissionEntry entry) in file.Missions ?? [])
        {
            if (!int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id))
            {
                throw new JsonException($"The ranks table names a mission '{key}', which is not a mission's number.");
            }

            entry.Check();
            missions[id] = entry;
        }

        return new Igi1MissionRanks(defaults, missions);
    }

    /// <summary>Gets what a mission's ranks ask, by the table.</summary>
    /// <param name="missionId">The mission.</param>
    /// <returns>The requirements, or <see langword="null"/> for a mission the table does not rank.</returns>
    public MissionRankRequirements? For(int missionId) =>
        _missions.TryGetValue(missionId, out MissionEntry? entry) ? Resolve(entry) : null;

    /// <summary>Gets what a mission's ranks ask, by its own file where it has one, else by the table.</summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="ownJson">The text of the mission's own file, or <see langword="null"/> for none.</param>
    /// <returns>The requirements, or <see langword="null"/> for a mission neither ranks.</returns>
    /// <exception cref="JsonException">The mission's own file is not one this build reads.</exception>
    public MissionRankRequirements? For(int missionId, string? ownJson)
    {
        if (ownJson is null)
        {
            return For(missionId);
        }

        MissionEntry entry = JsonSerializer.Deserialize(ownJson, Igi1MissionRanksJson.Default.MissionEntry)
            ?? throw new JsonException("The mission's ranks file is empty.");
        CheckFormat(entry.Format);
        entry.Check();
        return Resolve(entry);
    }

    /// <summary>
    /// Gets what a mission's ranks ask as it is played: its own <see cref="FileName"/> where its folder has one, read through the
    /// game's content (a mod's first); else the table's entry.
    /// </summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="files">The game's content.</param>
    /// <param name="levelDirectory">The mission's folder, as <c>LOCAL:missions/location0/level1/</c>.</param>
    /// <param name="logger">Where a file that cannot be read is reported.</param>
    /// <returns>The requirements, or <see langword="null"/> for a mission that is not ranked.</returns>
    public MissionRankRequirements? Read(int missionId, VirtualFileSystem files, string levelDirectory, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(levelDirectory);
        string path = levelDirectory + FileName;
        if (!files.TryOpenRead(path, out Stream? stream))
        {
            return For(missionId);
        }

        try
        {
            using var reader = new StreamReader(stream);
            return For(missionId, reader.ReadToEnd());
        }
        catch (JsonException error)
        {
            logger?.Warning("Mission", $"'{path}' cannot be read ({error.Message}); mission {missionId} is ranked by the game's table.");
            return For(missionId);
        }
    }

    private MissionRankRequirements? Resolve(MissionEntry entry)
    {
        if (entry.Par is not { } par)
        {
            return null;
        }

        RankRows own = entry.Ranks ?? new RankRows();
        float[] time = own.Time ?? _defaults.Time!;
        float[] spotted = own.Spotted ?? _defaults.Spotted!;
        float[] loads = own.Loads ?? _defaults.Loads!;
        float[] health = own.Health ?? _defaults.Health!;
        float[] accuracy = own.Accuracy ?? _defaults.Accuracy!;
        var requirements = new MissionRankRequirements();
        for (int rank = 0; rank < MissionRankRequirements.RankCount; rank++)
        {
            requirements[rank, MissionRankItem.Time] = par * time[rank];
            requirements[rank, MissionRankItem.Spotted] = spotted[rank];
            requirements[rank, MissionRankItem.Loads] = loads[rank];
            requirements[rank, MissionRankItem.Health] = health[rank];
            requirements[rank, MissionRankItem.Accuracy] = accuracy[rank];
        }

        return requirements;
    }

    private static void CheckFormat(int format)
    {
        if (format != CurrentFormat)
        {
            throw new JsonException($"The ranks are in format {format}; this build reads format {CurrentFormat}.");
        }
    }

    private static string ReadShipped()
    {
        using Stream stream = typeof(Igi1MissionRanks).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The game's ranks table ({ResourceName}) is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The whole table as written.</summary>
    /// <remarks>Setters rather than <c>init</c>: the generated reader would otherwise overwrite a default a file leaves out.</remarks>
    internal sealed class TableFile
    {
        public int Format { get; set; }

        public RankRows? Ranks { get; set; }

        public Dictionary<string, MissionEntry>? Missions { get; set; }
    }

    /// <summary>One mission's entry, or a mission's own file.</summary>
    internal sealed class MissionEntry
    {
        public int Format { get; set; } = CurrentFormat;

        public string? Name { get; set; }

        public float? Par { get; set; }

        public RankRows? Ranks { get; set; }

        public void Check()
        {
            if (Par is <= 0f)
            {
                throw new JsonException($"A mission's par time must be above nought; '{Name}' has {Par}.");
            }

            Ranks?.Check(complete: false);
        }
    }

    /// <summary>The three ranks' values for each figure, Bodyguard first.</summary>
    internal sealed class RankRows
    {
        public float[]? Time { get; set; }

        public float[]? Spotted { get; set; }

        public float[]? Loads { get; set; }

        public float[]? Health { get; set; }

        public float[]? Accuracy { get; set; }

        public void Check(bool complete)
        {
            foreach ((string name, float[]? row) in new[] { ("time", Time), ("spotted", Spotted), ("loads", Loads), ("health", Health), ("accuracy", Accuracy) })
            {
                if (row is null ? complete : row.Length != MissionRankRequirements.RankCount)
                {
                    throw new JsonException($"'{name}' must hold {MissionRankRequirements.RankCount} values, one for each rank.");
                }
            }
        }
    }
}

/// <summary>The JSON of <see cref="Igi1MissionRanks"/>, generated ahead of time.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(Igi1MissionRanks.TableFile))]
[JsonSerializable(typeof(Igi1MissionRanks.MissionEntry))]
internal sealed partial class Igi1MissionRanksJson : JsonSerializerContext;
