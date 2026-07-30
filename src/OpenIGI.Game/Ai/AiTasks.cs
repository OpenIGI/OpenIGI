using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Ai;

/// <summary>
/// A patrol path — the original's <c>PatrolPath</c>, whose steps are its child tasks.
/// </summary>
/// <remarks>
/// <para>
/// Registered at <c>0x453550</c> with an instance size of 52 and <b>no script parameters at all</b>
/// (<c>Task_DeclareParameters("PatrolPath");</c>). That is not an oversight: a path's identity <em>is</em>
/// its task id, which the post-load handler <c>0x453680</c> reads with <c>Task_GetId</c> and stores at
/// <c>+40</c> before linking the task onto a global list at <c>0x5BDA70</c>. <c>AIAction_Patrol</c>'s first
/// argument is that id, and <c>sub_453840</c> is the walk of that list looking for it.
/// </para>
/// <para>
/// The private list exists because the engine has no reverse index from a type to its instances — the id
/// table maps id to task for every type at once, and a patrol id has to resolve to a <c>PatrolPath</c>
/// specifically.
/// </para>
/// </remarks>
public sealed class PatrolPathTask : GameTask
{
}

/// <summary>One step of a patrol path — the original's <c>PatrolPathCommand</c>.</summary>
/// <remarks>
/// Registered at <c>0x4533B0</c>, 40 bytes, with two <c>Int32</c> parameters that the registrar itself
/// places at <c>+32</c> and <c>+36</c> — the same two offsets the kind and operand accessors
/// (<c>sub_453540</c>, <c>sub_485320</c>) read.
/// </remarks>
public partial class PatrolPathCommandTask : GameTask
{
    /// <summary>Gets or sets the command kind — <c>+32</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, AiPatrolCommand.KindParameter)]
    public int Command { get; set; }

    /// <summary>Gets or sets the command's operand — <c>+36</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, AiPatrolCommand.OperandParameter)]
    public int CommandParameter { get; set; }

    /// <summary>Gets the step as a typed command.</summary>
    public AiPatrolCommand Step => new((AiPatrolCommandKind)Command, CommandParameter);
}

/// <summary>A soldier's brain — the original's <c>HumanAI</c>, a 16080-byte child of its body.</summary>
/// <remarks>
/// <para>
/// Its two script parameters answer the question the milestone plan was written assuming had no answer:
/// <b>the AI type is an ordinary parameter</b>, carrying the <c>AITYPE_*</c> constant's <em>name</em> as a
/// string. Level data reads
/// <c>Task_New(1506, "HumanAI", "", "AITYPE_PATROL_AK", 1)</c>, and the resolved number lands at
/// <c>HumanAI+15672</c> (<c>ai.md</c> §3.6), from where §2.3's map picks one of the nine archetype scripts.
/// </para>
/// <para>
/// The graph id beside it selects which <c>graph&lt;n&gt;.dat</c> the AI walks — the id of the
/// <c>AIGraph</c> task that loaded it, which is why level 1 ships a <c>graph4019</c> beside its
/// <c>graph1</c> (§1.1).
/// </para>
/// </remarks>
public partial class HumanAiTask : GameTask
{
    /// <summary>Gets or sets the AI type, as the constant name level data spells it.</summary>
    /// <remarks>For example <c>AITYPE_PATROL_AK</c>; <see cref="ResolvedType"/> turns it into a number.</remarks>
    [ScriptParam(ScriptParamType.String32, "AI Type")]
    public string AiTypeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the id of the <c>AIGraph</c> task whose graph this AI walks.</summary>
    [ScriptParam(ScriptParamType.Int32, "Graph ID")]
    public int GraphId { get; set; }

    /// <summary>Gets the resolved AI type, or <see langword="null"/> when the name is not one of the 29.</summary>
    /// <remarks>
    /// Resolved through <see cref="AiScriptApi.Constants"/>, which is the same table the script VM would
    /// resolve the constant against — so a level naming a type the engine does not register reads as
    /// <see langword="null"/> here exactly as it would fail there.
    /// </remarks>
    public AiType? ResolvedType =>
        AiScriptApi.Constants.TryGetValue(AiTypeName, out int value) && Enum.IsDefined((AiType)value)
            ? (AiType)value
            : null;

    /// <summary>Gets the archetype script this AI runs, or <see langword="null"/> when the type is unknown.</summary>
    /// <remarks>
    /// The whole of <c>AIFunction_DefaultHandler</c> is <c>Script_Run(scripts[archetypeOf[aiType]])</c>, so
    /// this one lookup is what decides a soldier's entire behaviour.
    /// </remarks>
    public AiArchetype? Archetype => ResolvedType is { } type ? AiScriptApi.ArchetypeOf(type) : null;
}

/// <summary>Registers the AI task types.</summary>
/// <remarks>
/// <para>
/// As with the player types, the original's registrars declare the class and its message handlers while
/// the <b>parameter lists come from the level script</b>. The lists here were transcribed from the
/// <c>Task_DeclareParameters</c> calls in retail level data and are checked against every level's
/// declaration by <c>RetailPatrolPathTests</c>, not against this file.
/// </para>
/// <para>
/// <c>PatrolPath</c> is the one type in the game that declares <em>no</em> parameters, which is why
/// <see cref="PatrolPathTask"/> has none — see the original.
/// </para>
/// </remarks>
public static class AiTaskTypes
{
    /// <summary>The script name of the patrol path type.</summary>
    public const string PatrolPath = "PatrolPath";

    /// <summary>The script name of the patrol step type.</summary>
    public const string PatrolPathCommand = "PatrolPathCommand";

    /// <summary>The script name of the soldier brain type.</summary>
    public const string HumanAi = "HumanAI";

    /// <summary>Registers every AI task type.</summary>
    /// <param name="types">The registry to add to.</param>
    /// <param name="parent">The base type these derive from, if the host has one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterAll(TaskTypeRegistry types, TaskType? parent = null)
    {
        ArgumentNullException.ThrowIfNull(types);

        types.Register<PatrolPathTask>(PatrolPath, parent);
        types.Register<PatrolPathCommandTask>(PatrolPathCommand, parent);
        types.Register<HumanAiTask>(HumanAi, parent);
    }
}
