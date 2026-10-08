using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>Whether a level lets its player read the map computer's map.</summary>
/// <remarks>
/// <para>
/// HumanPlayerInput's update (<c>0x45DCF0</c>) stores <c>(__int64)</c> of its "Mapcomputer on expression"
/// (<c>0x4F16E0</c>, a floating-point evaluation) as the flag every computer page reads (<c>0x46C0B0</c>),
/// and 1 when the slot holds no expression (<c>0x4F16C0</c>). The computer opens either way: with the flag
/// clear <c>0x46BB10</c> gives it no mapcomputer model and opens its objectives page (mode 2 of
/// <c>0x46AFB0</c>), and neither text page offers a MAP link (<c>0x468920</c>, <c>0x468E90</c>).
/// </para>
/// <para>
/// Most levels leave the slot empty. Missions 7 and 14 write <c>0</c>; mission 8 waits for
/// <c>GenericPickup_860.isPickedUp</c>; mission 13 reads <c>!AreaActivate_3999.nActive</c>.
/// </para>
/// </remarks>
public sealed class MapComputerGate
{
    private readonly HumanPlayerInputTask? _input;
    private readonly LevelExpressionRuntime _expressions;

    /// <summary>Initializes a new instance of the <see cref="MapComputerGate"/> class.</summary>
    /// <param name="levelRoot">The level whose HumanPlayerInput carries the expression.</param>
    /// <param name="variables">The level's exported variables, which the expression reads.</param>
    public MapComputerGate(GameTask levelRoot, ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(variables);
        LevelRoot = levelRoot;
        _input = FindPlayerInput(levelRoot);
        _expressions = new LevelExpressionRuntime(variables);
    }

    /// <summary>Gets the level this gate reads.</summary>
    public GameTask LevelRoot { get; }

    /// <summary>Gets whether the map is the player's to read right now.</summary>
    public bool MapAvailable =>
        _input is not { } input
        || !_expressions.TryEvaluateReal(input.MapComputerOnExpression, input, out double value)
        || (long)value != 0;

    private static HumanPlayerInputTask? FindPlayerInput(GameTask task)
    {
        if (task is HumanPlayerInputTask found)
        {
            return found;
        }

        foreach (GameTask child in task.Children)
        {
            if (FindPlayerInput(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
