namespace OpenIGI.Engine.Tasks;

/// <summary>
/// One step of a <see cref="TaskStateTrigger"/>: a condition on the state machine's flag word, and how
/// long the recogniser tolerates that condition failing before it gives up.
/// </summary>
/// <param name="RequireSet">
/// When <see langword="true"/> the step matches while every bit of <paramref name="Mask"/> is set; when
/// <see langword="false"/> it matches while none of them is.
/// </param>
/// <param name="Tolerance">
/// The number of further ticks the step survives once it has matched and then stops matching. Loaded
/// into the trigger's countdown when the recogniser advances <em>onto</em> this step.
/// </param>
/// <param name="Mask">The bits of the flag word the step tests.</param>
/// <remarks>
/// The original's record is 12 bytes: <c>{ u32 polarity; u8 tolerance; u32 mask }</c>. Polarity is
/// tested as <c>!= 0</c>, not as a boolean, so any non-zero value means "require set".
/// </remarks>
public readonly record struct TaskStateStep(bool RequireSet, byte Tolerance, uint Mask);

/// <summary>
/// A timed sequence of conditions over a state machine's flag word — the mechanism behind
/// double-tap-style transitions.
/// </summary>
/// <remarks>
/// <para>
/// The driver (<c>0x4EC1E0</c>) advances every trigger once per tick. A trigger that reaches the end of
/// its steps reports itself as the tick's fired trigger and resets. See
/// the original.
/// </para>
/// </remarks>
public sealed class TaskStateTrigger
{
    private readonly TaskStateStep[] _steps;

    /// <summary>Initializes a new instance of the <see cref="TaskStateTrigger"/> class.</summary>
    /// <param name="steps">The steps, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="steps"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="steps"/> is empty.</exception>
    public TaskStateTrigger(IReadOnlyList<TaskStateStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("A trigger needs at least one step.", nameof(steps));
        }

        _steps = [.. steps];
    }

    /// <summary>Gets the steps.</summary>
    public IReadOnlyList<TaskStateStep> Steps => _steps;

    /// <summary>Gets the number of steps that must match for the trigger to fire.</summary>
    public int Length => _steps.Length;

    /// <summary>Tests one step against a flag word.</summary>
    /// <param name="index">The step to test.</param>
    /// <param name="flags">The state machine's flag word.</param>
    /// <returns><see langword="true"/> when the step's condition holds.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a step.</exception>
    public bool Matches(int index, uint flags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _steps.Length);

        TaskStateStep step = _steps[index];
        return step.RequireSet ? (flags & step.Mask) == step.Mask : (flags & step.Mask) == 0;
    }

    /// <summary>Gets a step's tolerance.</summary>
    /// <param name="index">The step.</param>
    /// <returns>The tolerance in ticks.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a step.</exception>
    public byte ToleranceOf(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _steps.Length);
        return _steps[index].Tolerance;
    }
}
