using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Ai;

/// <summary>
/// Every soldier's combat numbers — the original's <c>HumanAIConfig</c>, a 29 × 3 table of
/// <c>(AI type, difficulty)</c> rows read out of <c>common/ai/settings.qsc</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>sub_406DF0</c> creates the container, clears 348 bytes of pointers at <c>+36</c> — 29 types × 3
/// difficulties — runs the script, files each declared item at <c>difficulty + 3 * aiType</c>, and then
/// fills every slot the script left empty with an item carrying the constructor's own defaults. So the
/// table is always complete, whether or not the script loads at all.
/// </para>
/// <para>
/// The difficulty in force is the <b>player profile's</b>, not the AI's — <c>sub_406F70</c> reads it
/// once per configured <c>HumanAI</c>.
/// </para>
/// </remarks>
public sealed class HumanAiConfigRegistry
{
    /// <summary>The script that carries the table.</summary>
    public const string ScriptResource = "LOCAL:common/ai/settings.qvm";

    /// <summary>The task type the rows hang off.</summary>
    public const string ConfigType = "HumanAIConfig";

    /// <summary>The task type one row is declared as.</summary>
    public const string ConfigItemType = "HumanAIConfigItem";

    /// <summary>How many AI types the table is wide.</summary>
    public const int AiTypeCount = 29;

    /// <summary>How many difficulties the table is deep.</summary>
    public const int DifficultyCount = 3;

    private const string LogCategory = "Ai";

    private static readonly string[] DifficultyNames = ["GD_1", "GD_2", "GD_3"];

    private readonly HumanAiConfigItemTask?[] _declared = new HumanAiConfigItemTask?[AiTypeCount * DifficultyCount];
    private readonly HumanAiConfigItemTask[] _rows = new HumanAiConfigItemTask[AiTypeCount * DifficultyCount];
    private readonly AiCombatParameters[] _resolved = new AiCombatParameters[AiTypeCount * DifficultyCount];
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="HumanAiConfigRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    /// <remarks>The table starts complete, filled with the engine's own default rows.</remarks>
    public HumanAiConfigRegistry(IOpenIgiLogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        FillGaps();
    }

    /// <summary>Gets how many rows the script declared.</summary>
    public int DeclaredRowCount => _declared.Count(row => row is not null);

    /// <summary>Gets how many rows fell back on the constructor's defaults.</summary>
    public int DefaultedRowCount => _declared.Length - DeclaredRowCount;

    /// <summary>Registers the two configuration task types.</summary>
    /// <param name="types">The registry to add to.</param>
    /// <param name="parent">The base type these derive from, if the host has one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterTypes(TaskTypeRegistry types, TaskType? parent = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        types.Register<HumanAiConfigTask>(ConfigType, parent);
        types.Register<HumanAiConfigItemTask>(ConfigItemType, parent);
    }

    /// <summary>Turns a <c>GD_n</c> name into a difficulty.</summary>
    /// <param name="name">The name, as the script spells it.</param>
    /// <returns>The difficulty, or <see langword="null"/> when the name is not one of the three.</returns>
    /// <remarks>The three names come from <c>sub_406330</c>, not from the script.</remarks>
    public static AiDifficulty? ParseDifficulty(string? name)
    {
        int index = Array.IndexOf(DifficultyNames, name);
        return index < 0 ? null : (AiDifficulty)index;
    }

    /// <summary>Gets the <c>GD_n</c> name of a difficulty.</summary>
    /// <param name="difficulty">The difficulty.</param>
    /// <returns>The name the script keys its rows by.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="difficulty"/> is not one of the three.</exception>
    public static string NameOf(AiDifficulty difficulty) =>
        (int)difficulty is >= 0 and < DifficultyCount
            ? DifficultyNames[(int)difficulty]
            : throw new ArgumentOutOfRangeException(nameof(difficulty));

    /// <summary>Reads and runs <c>common/ai/settings.qsc</c>.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace; the <c>AITYPE_*</c> constants need not be registered.</param>
    /// <param name="tree">The task tree the rows are built into.</param>
    /// <returns>How many rows the script filed.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script is missing, malformed, or faults.</exception>
    /// <remarks>
    /// A row naming a type or a difficulty the engine does not know is <b>dropped</b>, which is what the
    /// original's index arithmetic amounts to: the name is resolved to a number before it is used as an
    /// index, and there is no slot for a number outside the table.
    /// </remarks>
    public int Load(ResourceRegistry resources, NativeRegistry natives, TaskTree tree)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(tree);

        Array.Clear(_declared);

        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}");
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}", ex);
        }

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"{ScriptResource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        Collect(tree.Root);
        FillGaps();

        _logger.Information(
            LogCategory,
            $"Registered {DeclaredRowCount} AI configuration row(s); {DefaultedRowCount} defaulted.");

        return DeclaredRowCount;
    }

    /// <summary>Gets the row a type reads at a difficulty.</summary>
    /// <param name="type">The AI type.</param>
    /// <param name="difficulty">The difficulty in force.</param>
    /// <returns>The row; never <see langword="null"/>, because the table is always complete.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is outside the table.</exception>
    public HumanAiConfigItemTask Item(AiType type, AiDifficulty difficulty) => _rows[IndexOf(type, difficulty)];

    /// <summary>Gets the resolved combat block a type reads at a difficulty.</summary>
    /// <param name="type">The AI type.</param>
    /// <param name="difficulty">The difficulty in force.</param>
    /// <returns>The block, with every conversion <c>sub_406F70</c> applies already applied.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is outside the table.</exception>
    public AiCombatParameters Parameters(AiType type, AiDifficulty difficulty) =>
        _resolved[IndexOf(type, difficulty)];

    /// <summary>Tells whether a row came from the script rather than from the defaults.</summary>
    /// <param name="type">The AI type.</param>
    /// <param name="difficulty">The difficulty.</param>
    /// <returns><see langword="true"/> when the script declared it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is outside the table.</exception>
    public bool IsDeclared(AiType type, AiDifficulty difficulty) => _declared[IndexOf(type, difficulty)] is not null;

    private static int IndexOf(AiType type, AiDifficulty difficulty)
    {
        int typeIndex = (int)type;
        int difficultyIndex = (int)difficulty;

        ArgumentOutOfRangeException.ThrowIfNegative(typeIndex, nameof(type));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(typeIndex, AiTypeCount, nameof(type));
        ArgumentOutOfRangeException.ThrowIfNegative(difficultyIndex, nameof(difficulty));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(difficultyIndex, DifficultyCount, nameof(difficulty));

        // difficulty + 3 * aiType, which is the original's own layout: sequential over the difficulties
        // of one type, then on to the next type.
        return difficultyIndex + (DifficultyCount * typeIndex);
    }

    private void Collect(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is HumanAiConfigItemTask item
                && item.ResolvedType is { } type
                && item.ResolvedDifficulty is { } difficulty)
            {
                // The original overwrites, so a repeated key keeps the LAST declaration.
                _declared[IndexOf(type, difficulty)] = item;
            }

            Collect(child);
        }
    }

    private void FillGaps()
    {
        for (int type = 0; type < AiTypeCount; type++)
        {
            for (int difficulty = 0; difficulty < DifficultyCount; difficulty++)
            {
                int index = difficulty + (DifficultyCount * type);
                _rows[index] = _declared[index] ?? new HumanAiConfigItemTask
                {
                    AiTypeName = AiScriptApi.TypeName((AiType)type),
                    DifficultyName = DifficultyNames[difficulty],
                };

                _resolved[index] = new AiCombatParameters(_rows[index]);
            }
        }
    }
}
