namespace OpenIGI.Game.Ai;

/// <summary>
/// The step kinds a patrol path is built from. <c>AIAction_Patrol</c> does not walk graph nodes directly — it
/// runs a little programme, one step at a time, and each step drives the AI until it reports done.
/// </summary>
/// <remarks>
/// <para>
/// Read from the patrol per-tick <c>0x48E920</c>, whose switch has arms for 0–5 and 7. The debug format
/// strings the original left in name three of them outright: <c>"Patrolling, Standaction : %d"</c>,
/// <c>"Patrolling, Delay : %d"</c>, <c>"Patrolling, Walkto:%d, Cur:%d"</c>,
/// <c>"Patrolling, Runto:%d, Cur:%d"</c> and <c>"Patrolling, LookAtNode : %d"</c>.
/// </para>
/// <para>
/// <b>6 has no arm at all</b> — it falls through the switch and the step never advances, so a path containing
/// one would wedge. It is not unused, though: the route initialiser <c>0x4537E0</c> tests for exactly 6 on the
/// first step and skips it, which is what stops a path that opens with one from hanging. So 6 is a real kind
/// the walker cannot execute, handled only at the entry point.
/// </para>
/// </remarks>
public enum AiPatrolStepKind
{
    /// <summary>Play a standing action, the operand naming which. Advances when the animation reports done.</summary>
    StandAction = 0,

    /// <summary>Wait. The operand is a tick count, added to the current tick to form a deadline.</summary>
    Delay = 1,

    /// <summary>Walk to the graph node the operand names.</summary>
    WalkTo = 2,

    /// <summary>Run to the graph node the operand names.</summary>
    RunTo = 3,

    /// <summary>Set the path's own flag and advance immediately — the only kind that never waits.</summary>
    SetFlag = 4,

    /// <summary>Turn to face the graph node the operand names, to within 3°.</summary>
    LookAtNode = 5,

    /// <summary>
    /// Has no arm in the walker's switch. <c>0x4537E0</c> skips it if it is the first step; anywhere else it
    /// would wedge the patrol.
    /// </summary>
    Unexecutable = 6,

    /// <summary>End of the path — finishes the action.</summary>
    End = 7,
}

/// <summary>The mutable state a running patrol keeps — the 28-byte record <c>0x4537E0</c> initialises.</summary>
/// <remarks>
/// <para>
/// The records live in an array at <c>HumanAI+5400</c> with a count at <c>+5396</c>. Starting a patrol
/// <b>pushes</b> one and the per-tick always reads the <b>last</b> pushed, so patrols nest rather than replace.
/// </para>
/// <para>
/// <see cref="Deadline"/> doubles as "has this step begun": <c>-1</c> means not yet, and advancing a step
/// resets it to <c>-1</c>. That is how <see cref="AiPatrolStepKind.Delay"/> knows to compute its deadline once
/// rather than every tick.
/// </para>
/// </remarks>
public sealed class AiPatrolState
{
    /// <summary>The size of the record in bytes in the original.</summary>
    public const int Size = 28;

    /// <summary>The angular tolerance <see cref="AiPatrolStepKind.LookAtNode"/> accepts — 3°, as a float.</summary>
    public const float LookAtToleranceRadians = 0.052359879f;

    /// <summary>Gets or sets the path's own flag, set by <see cref="AiPatrolStepKind.SetFlag"/> — <c>+20</c>.</summary>
    /// <remarks>
    /// Cleared by both movement kinds. While set, the per-tick calls the pose hook with <c>(1, 1.0)</c> before
    /// anything else, and it changes which variant of the stand-action test is used.
    /// </remarks>
    public bool Flag { get; set; }

    /// <summary>Gets or sets whether the AI is walking rather than running — <c>+21</c>.</summary>
    /// <remarks>
    /// Starts <b>true</b> (a patrol walks by default), is set by <see cref="AiPatrolStepKind.WalkTo"/> and
    /// cleared by <see cref="AiPatrolStepKind.RunTo"/>, and selects which gait call the per-tick makes on
    /// <em>every</em> tick — so the gait outlives the step that set it.
    /// </remarks>
    public bool Walking { get; set; } = true;

    /// <summary>Gets or sets the tick a <see cref="AiPatrolStepKind.Delay"/> ends on, or <c>-1</c> — <c>+24</c>.</summary>
    public int Deadline { get; set; } = -1;

    /// <summary>Advances to the next step, as the walker's common tail does.</summary>
    public void Advance() => Deadline = -1;

    /// <summary>Applies a step kind's own effect on this state, and says whether the step waits.</summary>
    /// <param name="kind">The step kind.</param>
    /// <returns><see langword="true"/> when the step completes on the tick it starts.</returns>
    /// <remarks>
    /// Only the state changes are here — the movement, animation and turning a step drives are elsewhere. The
    /// return value is what <see cref="AiPatrolStepKind.SetFlag"/> uses to advance without waiting.
    /// </remarks>
    public bool Begin(AiPatrolStepKind kind)
    {
        switch (kind)
        {
            case AiPatrolStepKind.WalkTo:
                Flag = false;
                Walking = true;
                return false;

            case AiPatrolStepKind.RunTo:
                Flag = false;
                Walking = false;
                return false;

            case AiPatrolStepKind.SetFlag:
                Flag = true;
                return true;

            default:
                return false;
        }
    }
}
