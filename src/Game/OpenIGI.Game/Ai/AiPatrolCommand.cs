namespace OpenIGI.Game.Ai;

/// <summary>
/// The nine commands a patrol path is built from — the <c>PATROLPATHCOMMAND_*</c> constant family.
/// </summary>
/// <remarks>
/// <para>
/// Registered by the tail of <c>PatrolPath</c>'s own registrar (<c>0x453550</c>), which walks the
/// pointer array at <c>off_53E2A0</c> handing out <c>i - 1</c> — so the first constant is
/// <b><see cref="None"/> = −1</b>, not zero. The array is in descending string-address order, the same
/// layout as <c>AIEVENT_*</c>, so it has to be read as an array rather than off the string table.
/// </para>
/// <para>
/// A command is a <c>PatrolPathCommand</c> task (40 bytes, <c>0x4533B0</c>) whose two <c>Int32</c> script
/// parameters sit at <c>+32</c> and <c>+36</c> — the kind and its operand, which is exactly what the
/// accessors <c>sub_453540</c> and <c>sub_485320</c> read. Retail level data declares them
/// <c>"Command"</c> and <c>"Command Parameter"</c>.
/// </para>
/// <para>
/// <b>Two walkers consume these and neither implements all nine.</b> The human walker
/// (<c>0x48E920</c>) has arms for 0–5 and 7; <c>CarAI</c>'s (<c>0x425DE0</c>) has 1, 2, 3, 6, 7 and 8.
/// See <see cref="AiPatrolCommandKindExtensions.IsExecutableByAHuman"/> for what that costs.
/// </para>
/// </remarks>
public enum AiPatrolCommandKind
{
    /// <summary>No command — the value a <c>PatrolPathCommand</c> carries when nothing was authored.</summary>
    /// <remarks>
    /// −1 rather than 0, because the registrar's loop starts its counter at −1. It appears in retail data
    /// as the operand of an <see cref="End"/>, never as a kind.
    /// </remarks>
    None = -1,

    /// <summary>Play the animation the operand names, and wait for it to finish.</summary>
    /// <remarks>
    /// The walker's debug string calls this <c>"Patrolling, Standaction : %d"</c> and an earlier pass of
    /// this port took its name from there. The constant is <c>PATROLPATHCOMMAND_ANIMATION</c> and the
    /// retail label on level 1's is <c>"Plays predefined animation 240"</c>: both describe the same arm,
    /// but the constant is the author's name for it.
    /// </remarks>
    Animation = 0,

    /// <summary>Wait. The operand is a tick count, added to the current tick to form a deadline.</summary>
    Delay = 1,

    /// <summary>Walk to the graph node the operand names. Sets the gait to walking.</summary>
    WalkTo = 2,

    /// <summary>Run to the graph node the operand names. Sets the gait to running.</summary>
    RunTo = 3,

    /// <summary>Crouch, and advance on the same tick — the only kind that never waits.</summary>
    /// <remarks>
    /// Sets <see cref="AiPatrolRoute.IsCrouching"/>, which the walker applies on every subsequent tick and
    /// which only a movement command clears.
    /// </remarks>
    Crouch = 4,

    /// <summary>Turn to face the graph node the operand names, to within 3°.</summary>
    LookAtNode = 5,

    /// <summary>
    /// Marks where the looping body of the path begins. Never executed — the cursor consumes it.
    /// </summary>
    /// <remarks>
    /// This is the kind whose name mattered most. It has no arm in either walker's switch, which an
    /// earlier pass read as "a kind that cannot be executed"; in fact the commands run to the end of the
    /// list and then wrap back to the command <em>after</em> this one, so everything before it is a
    /// run-once prologue. Retail data says so in words — the label on level 1's is
    /// <c>"End script, only runs commands after this one."</c>
    /// </remarks>
    End = 6,

    /// <summary>Finish the patrol action outright.</summary>
    Quit = 7,

    /// <summary>Set the drive speed to the operand. <b>Vehicles only.</b></summary>
    /// <remarks>
    /// The human walker has no arm for this and its <c>default:</c> returns <em>without advancing the
    /// cursor</em>, so a <see cref="SetSpeed"/> in a human's path stops that patrol permanently. See
    /// <see cref="AiPatrolCommandKindExtensions.IsExecutableByAHuman"/>.
    /// </remarks>
    SetSpeed = 8,
}

/// <summary>Helpers over <see cref="AiPatrolCommandKind"/>.</summary>
public static class AiPatrolCommandKindExtensions
{
    /// <summary>Whether the human patrol walker has an arm for this kind.</summary>
    /// <param name="kind">The command kind.</param>
    /// <returns><see langword="true"/> when <c>0x48E920</c> can execute it.</returns>
    /// <remarks>
    /// <para>
    /// The walker's <c>default:</c> arm returns without advancing the cursor.
    /// <see cref="AiPatrolCommandKind.End"/> never
    /// reaches it (the cursor consumes it first) and 0–5 and 7 all have arms, which leaves exactly one
    /// kind that can: <see cref="AiPatrolCommandKind.SetSpeed"/>. A human patrol that reaches one jams on
    /// it forever.
    /// </para>
    /// <para>
    /// <c>CarAI</c> is the mirror image — its default arm logs
    /// <c>"Command not supported by CARAI: %d."</c> and <b>does</b> advance, so the vehicle side skips
    /// what it cannot do while the human side wedges. Only one of the two authors defended against it.
    /// </para>
    /// </remarks>
    public static bool IsExecutableByAHuman(this AiPatrolCommandKind kind) =>
        kind is AiPatrolCommandKind.Animation
            or AiPatrolCommandKind.Delay
            or AiPatrolCommandKind.WalkTo
            or AiPatrolCommandKind.RunTo
            or AiPatrolCommandKind.Crouch
            or AiPatrolCommandKind.LookAtNode
            or AiPatrolCommandKind.Quit;

    /// <summary>Whether the kind moves the AI towards a graph node.</summary>
    /// <param name="kind">The command kind.</param>
    /// <returns><see langword="true"/> for <see cref="AiPatrolCommandKind.WalkTo"/> and
    /// <see cref="AiPatrolCommandKind.RunTo"/>.</returns>
    /// <remarks>
    /// These are the two the cursor remembers, keeping the last and the one before it, and the two that
    /// clear the crouch flag.
    /// </remarks>
    public static bool IsMovement(this AiPatrolCommandKind kind) =>
        kind is AiPatrolCommandKind.WalkTo or AiPatrolCommandKind.RunTo;
}

/// <summary>One step of a patrol path — a <c>PatrolPathCommand</c> task's two parameters.</summary>
/// <param name="Kind">The command, from <c>+32</c>.</param>
/// <param name="Operand">
/// Its argument, from <c>+36</c>: an animation id, a tick count, a graph node id or a speed, depending on
/// <paramref name="Kind"/>. Unused kinds carry −1.
/// </param>
public readonly record struct AiPatrolCommand(AiPatrolCommandKind Kind, int Operand)
{
    /// <summary>The script parameter name level data gives the kind.</summary>
    public const string KindParameter = "Command";

    /// <summary>The script parameter name level data gives the operand.</summary>
    public const string OperandParameter = "Command Parameter";
}
