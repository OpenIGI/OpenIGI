using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Player;

/// <summary>
/// The two triggers the locomotion state machine watches — both of them double-taps.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanPlayer</c>'s constructor registers the locomotion machine with two triggers and the table at
/// <c>0x538710</c>. Each trigger is four steps of twelve bytes, and the two step arrays sit
/// <b>immediately before</b> the trigger array and end exactly where it begins — 0x5386B0, 0x5386E0,
/// 0x538710 — which is what fixes both the step count and the record size.
/// </para>
/// <para>
/// Both have the same shape over a single mask bit: <b>set, clear, set, clear</b>, with a tolerance of
/// <see cref="WindowTicks"/> on each step after the first. That is a double-tap recogniser, and the
/// tolerance is the window each phase must complete in.
/// </para>
/// </remarks>
public static class HumanLocomotionTriggers
{
    /// <summary>The number of triggers the locomotion machine registers.</summary>
    public const int Count = 2;

    /// <summary>The flag bit the first trigger watches — action bit <c>0x200</c>.</summary>
    public const uint FirstMask = 0x200;

    /// <summary>The flag bit the second trigger watches — action bit <c>0x80000</c>.</summary>
    public const uint SecondMask = 0x80000;

    /// <summary>
    /// The tolerance on every step after the first — six ticks, i.e. <b>0.2 s</b> at 30 Hz.
    /// </summary>
    /// <remarks>
    /// Each phase of the gesture has this long to complete. Hold the button past it and the trigger
    /// resets; tap again too late and it resets. The first step carries a tolerance of zero because
    /// nothing has started yet, and a mismatch on step 0 costs nothing.
    /// </remarks>
    public const byte WindowTicks = 6;

    /// <summary>Builds a double-tap trigger over one flag bit.</summary>
    /// <param name="mask">The bit to watch.</param>
    /// <returns>The four-step trigger.</returns>
    public static TaskStateTrigger DoubleTap(uint mask) =>
        new([
            new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: mask),
            new TaskStateStep(RequireSet: false, Tolerance: WindowTicks, Mask: mask),
            new TaskStateStep(RequireSet: true, Tolerance: WindowTicks, Mask: mask),
            new TaskStateStep(RequireSet: false, Tolerance: WindowTicks, Mask: mask),
        ]);

    /// <summary>Builds the two triggers in the order the machine registers them.</summary>
    /// <returns>The triggers; index 0 fires as <c>FiredTrigger == 0</c>.</returns>
    public static IReadOnlyList<TaskStateTrigger> Create() =>
        [DoubleTap(FirstMask), DoubleTap(SecondMask)];
}
