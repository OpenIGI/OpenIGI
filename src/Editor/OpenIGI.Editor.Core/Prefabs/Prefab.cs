using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Editor.Core.Prefabs;

/// <summary>
/// A piece of a mission kept to be placed again, in this mission or another of the same game: tasks with everything under
/// them, written as the script that makes them, the place they stood round, and each soldier's mind (its AI script).
/// </summary>
/// <remarks>
/// Placed, every task that had an id takes a free one and what the copies say about each other follows them
/// (<see cref="MissionDocument.ReadTasksAsNew"/>), every position moves with the piece, and each mind is written again under
/// its soldier's new id, so a guard placed from a prefab keeps his orders. References to tasks outside the piece (an AI
/// graph, the alarm a guard runs to) are kept as they are: the same mission's own, and for another mission the validator
/// says what is missing.
/// </remarks>
public sealed record Prefab
{
    /// <summary>The type of the task a soldier's mind is, whose id names its script (<c>ai/&lt;id&gt;.qvm</c>).</summary>
    public const string MindType = "HumanAI";

    /// <summary>Gets what it is called.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the shelf it is kept on in the library: soldiers, buildings, logic and the like.</summary>
    public string Category { get; init; } = "Other";

    /// <summary>Gets the game it is for (<c>igi1</c>, <c>igi2</c>): a piece of one does not place in the other.</summary>
    public required string Game { get; init; }

    /// <summary>Gets what it holds, in a line.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the point it stood round, in world units: the middle of its positions across, the lowest of them up.</summary>
    public required double[] Anchor { get; init; }

    /// <summary>Gets the script that makes its tasks (<see cref="MissionDocument.WriteTasks"/>).</summary>
    public required string Script { get; init; }

    /// <summary>Gets each soldier's mind by the id its <c>HumanAI</c> had, as base64 of the script's bytes.</summary>
    public Dictionary<int, string> Minds { get; init; } = [];

    /// <summary>Keeps tasks as a prefab.</summary>
    /// <param name="document">The mission they are in.</param>
    /// <param name="tasks">The tasks; any inside another of them goes with it rather than twice.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="game">The game it is for.</param>
    /// <param name="mind">Reads a soldier's mind by its id, or null where it has none.</param>
    /// <returns>The prefab; null when no task is given.</returns>
    public static Prefab? Capture(MissionDocument document, IEnumerable<MissionTask> tasks, string name, string game, Func<int, byte[]?> mind)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(mind);
        MissionTask[] all = [.. tasks.Distinct()];
        MissionTask[] roots = [.. all.Where(t => !all.Any(other => !ReferenceEquals(other, t) && IsWithin(t, other)))];
        if (roots.Length == 0)
        {
            return null;
        }

        MissionTask[] inside = [.. roots.SelectMany(static t => t.SelfAndSubtree())];
        double[][] places = [.. inside.Select(Place).OfType<double[]>()];
        double[] anchor = places.Length == 0
            ? [0, 0, 0]
            : [places.Average(static p => p[0]), places.Average(static p => p[1]), places.Min(static p => p[2])];
        Dictionary<int, string> minds = [];
        foreach (MissionTask ai in inside.Where(static t => t.TypeName == MindType && t.Id >= 0))
        {
            if (mind(ai.Id) is { } bytes)
            {
                minds[ai.Id] = Convert.ToBase64String(bytes);
            }
        }

        return new Prefab
        {
            Name = name,
            Game = game,
            Category = CategoryOf(inside),
            Description = Describe(inside),
            Anchor = anchor,
            Script = document.WriteTasks(roots),
            Minds = minds,
        };
    }

    /// <summary>Reads a prefab from its file's text.</summary>
    /// <param name="text">The text, as <see cref="Write"/> wrote it.</param>
    /// <returns>The prefab.</returns>
    /// <exception cref="JsonException">The text is not a prefab.</exception>
    public static Prefab Read(string text) => JsonSerializer.Deserialize(text, PrefabJson.Default.Prefab) ?? throw new JsonException("The file holds no prefab.");

    /// <summary>Writes the prefab as its file's text.</summary>
    /// <returns>The text.</returns>
    public string Write() => JsonSerializer.Serialize(this, PrefabJson.Default.Prefab);

    /// <summary>The prefab's tasks made new for a mission, standing round a point.</summary>
    /// <param name="document">The mission they go into.</param>
    /// <param name="at">Where its anchor goes, in world units.</param>
    /// <returns>The tasks ready to insert, the types the mission cannot take, and each mind to write by its new id.</returns>
    /// <exception cref="Scripting.Qsc.QscParseException">The prefab's script does not read.</exception>
    public PlacedPrefab Place(MissionDocument document, (double X, double Y, double Z) at)
    {
        ArgumentNullException.ThrowIfNull(document);
        PastedTasks pasted = document.ReadTasksAsNew(Script, out IReadOnlyDictionary<int, int> original);
        double[] shift = [at.X - Anchor[0], at.Y - Anchor[1], at.Z - Anchor[2]];
        Dictionary<int, byte[]> minds = [];
        foreach (MissionTask task in pasted.Tasks.SelectMany(static t => t.SelfAndSubtree()))
        {
            foreach (MissionParameter parameter in task.Parameters.Where(static p => p.Declaration.Type == ScriptParamType.ObjectPos))
            {
                if (parameter.TryGetNumbers(out double[] position) && position.Length == 3)
                {
                    parameter.SetNumbers([position[0] + shift[0], position[1] + shift[1], position[2] + shift[2]]);
                }
            }

            if (task.TypeName == MindType && original.TryGetValue(task.Id, out int was) && Minds.TryGetValue(was, out string? script))
            {
                minds[task.Id] = Convert.FromBase64String(script);
            }
        }

        return new PlacedPrefab(pasted.Tasks, pasted.Skipped, minds);
    }

    // Where a task stands, in world units, when it has a place.
    private static double[]? Place(MissionTask task) =>
        task.FindParameter("Position") is { Declaration.Type: ScriptParamType.ObjectPos } position && position.TryGetNumbers(out double[] at) && at.Length == 3 ? at : null;

    private static bool IsWithin(MissionTask task, MissionTask ancestor)
    {
        for (MissionTask? parent = task.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    // The shelf: what most of the piece is.
    private static string CategoryOf(IReadOnlyCollection<MissionTask> tasks)
    {
        if (tasks.Any(static t => t.TypeName.StartsWith("HumanSoldier", StringComparison.Ordinal)))
        {
            return "Soldiers";
        }

        if (tasks.Any(static t => t.TypeName is "SCamera" or "AlarmControl" or "Siren" or "AlarmLight"))
        {
            return "Security";
        }

        if (tasks.Any(static t => t.TypeName is "Car" or "Helicopter" or "Tank" or "Truck" or "Train"))
        {
            return "Vehicles";
        }

        if (tasks.Any(static t => t.TypeName == "Building"))
        {
            return "Buildings";
        }

        return tasks.All(static t => Place(t) is null) ? "Logic" : "Objects";
    }

    // What it holds, most first: "3 HumanSoldier, 2 Building".
    private static string Describe(IEnumerable<MissionTask> tasks) =>
        string.Join(", ", tasks
            .Where(static t => t.TypeName is not ("Container" or MindType) && !t.TypeName.StartsWith("Gun", StringComparison.Ordinal))
            .GroupBy(static t => t.TypeName)
            .OrderByDescending(static g => g.Count())
            .Take(4)
            .Select(static g => string.Create(CultureInfo.InvariantCulture, $"{g.Count()} {g.Key}")));
}

/// <summary>A prefab's file shape, camelCase, generated so that reading one needs no reflection.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(Prefab))]
internal sealed partial class PrefabJson : JsonSerializerContext;

/// <summary>A prefab made new for a mission.</summary>
/// <param name="Tasks">The tasks to insert, new and unattached.</param>
/// <param name="Skipped">The types left out because the mission does not declare them.</param>
/// <param name="Minds">Each soldier's mind to write, by its new id.</param>
public sealed record PlacedPrefab(IReadOnlyList<MissionTask> Tasks, IReadOnlyList<string> Skipped, IReadOnlyDictionary<int, byte[]> Minds);

/// <summary>The prefabs kept on disk, a file each, in a folder a game.</summary>
/// <param name="directory">The folder the games' folders are in.</param>
public sealed class PrefabLibrary(string directory)
{
    /// <summary>The ending of a prefab's file.</summary>
    public const string Extension = ".prefab";

    /// <summary>Every prefab kept for a game, by name; a file that does not read is passed over.</summary>
    /// <param name="game">The game.</param>
    /// <returns>The prefabs.</returns>
    public IReadOnlyList<Prefab> List(string game)
    {
        string folder = Path.Combine(directory, game);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        List<Prefab> found = [];
        foreach (string file in Directory.EnumerateFiles(folder, "*" + Extension))
        {
            try
            {
                found.Add(Prefab.Read(File.ReadAllText(file)));
            }
            catch (Exception error) when (error is JsonException or IOException or NotSupportedException)
            {
                // A damaged or foreign file is not a prefab; the rest still are.
            }
        }

        return [.. found.OrderBy(static p => p.Category, StringComparer.Ordinal).ThenBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Keeps a prefab, replacing one of the same name.</summary>
    /// <param name="prefab">The prefab.</param>
    /// <returns>The file it went to.</returns>
    public string Save(Prefab prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        string folder = Path.Combine(directory, prefab.Game);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, FileName(prefab.Name) + Extension);
        File.WriteAllText(file, prefab.Write());
        return file;
    }

    /// <summary>Throws a prefab away.</summary>
    /// <param name="prefab">The prefab.</param>
    public void Delete(Prefab prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        File.Delete(Path.Combine(directory, prefab.Game, FileName(prefab.Name) + Extension));
    }

    // A name made safe for a file: what a file name cannot hold becomes a dash.
    private static string FileName(string name)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        string safe = new([.. name.Trim().Select(c => bad.Contains(c) ? '-' : c)]);
        return safe.Length == 0 ? "Prefab" : safe;
    }
}
