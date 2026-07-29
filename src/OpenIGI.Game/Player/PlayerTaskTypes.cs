using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Player;

/// <summary>
/// Registers the player task types.
/// </summary>
/// <remarks>
/// <para>
/// The original registers each in its own function — <c>Human</c> at <c>0x45E450</c>,
/// <c>HumanPlayer</c> at <c>0x40FE30</c>, <c>HumanPlayerInput</c> at <c>0x45DB70</c>. What those
/// functions declare is the class and its message handlers; the <b>parameter lists come from the level
/// script</b>, exactly as the menu types' do, and every level's <c>objects.qvm</c> declares the same
/// ones.
/// </para>
/// <para>
/// <b>Parameter order is wire order.</b> The lists on the task classes were transcribed from the
/// <c>Task_DeclareParameters</c> calls in retail level data, and <c>Task_New</c> parses positionally —
/// one wrong entry shifts every field after it and silently swallows the following child tasks.
/// <c>RetailPlayerTaskTests</c> checks them against the script rather than against this file.
/// </para>
/// </remarks>
public static class PlayerTaskTypes
{
    /// <summary>The script name of the player's character type.</summary>
    public const string HumanPlayer = "HumanPlayer";

    /// <summary>The script name of the AI character type.</summary>
    public const string HumanSoldier = "HumanSoldier";

    /// <summary>The script name of the player's input source.</summary>
    public const string HumanPlayerInput = "HumanPlayerInput";

    /// <summary>Registers every player task type.</summary>
    /// <param name="types">The registry to add to.</param>
    /// <param name="parent">The base type these derive from, if the host has one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterAll(TaskTypeRegistry types, TaskType? parent = null)
    {
        ArgumentNullException.ThrowIfNull(types);

        types.Register<HumanPlayerTask>(HumanPlayer, parent);
        types.Register<HumanSoldierTask>(HumanSoldier, parent);
        types.Register<HumanPlayerInputTask>(HumanPlayerInput, parent);
    }
}
