namespace OpenIGI.Game.Ai;

/// <summary>
/// An <c>AlarmControl</c>'s two-tick alarm state, and the rule that turns it into
/// <see cref="AiEvent.AlarmOn"/> and <see cref="AiEvent.AlarmOff"/>.
/// </summary>
/// <remarks>
/// <para>
/// From the update at <c>0x4408A0</c>. The task keeps <b>two</b> flags — <c>+673</c> is the alarm's state this
/// tick and <c>+674</c> is <b>the previous tick's value of <c>+673</c></b>, shadowed at the <em>top</em> of the
/// update before the new value is computed. It is the same <c>Last</c>-shadow edge-detection pattern M3 found
/// in the script variable registry, and the determinism contract already requires reproducing it.
/// </para>
/// <para>
/// <b>That corrects a misreading of the two accessors.</b> <c>0x440C00</c> and <c>0x440C10</c> look like
/// "is on" and "is off"; they are <b>is on</b> and <b>was on</b>. The consequence is the interesting part —
/// the AI's per-tick does
/// </para>
/// <code>
/// if      (IsOn(ctrl))  { post AIEVENT_ALARMON;  alert = 1; }
/// else if (WasOn(ctrl)) { post AIEVENT_ALARMOFF; }
/// </code>
/// <para>
/// so <b><see cref="AiEvent.AlarmOn"/> is level-triggered</b> — posted every tick the alarm is on, which is
/// also what keeps the AI's alert latch set — while <b><see cref="AiEvent.AlarmOff"/> is a falling edge</b>,
/// posted on exactly the one tick after the alarm stops. An AI that misses that single tick (because a
/// higher-priority event took the dispatch, and undispatched events are discarded) never hears the all-clear.
/// </para>
/// <para>
/// The state itself comes from <b>two script condition expressions</b>, which are the task type's only two
/// parameters and occupy the tail of its 848-byte instance exactly: one at <c>+684</c> (80 bytes) sets a
/// <see cref="IsTriggered"/> flag and stamps the tick into <c>+676</c>, and one at <c>+764</c> (84 bytes) sets
/// <see cref="IsOn"/>. Two disable gates at <c>+400</c> and <c>+576</c> force both to false without evaluating
/// either.
/// </para>
/// </remarks>
public sealed class AiAlarmControlState
{
    /// <summary>The instance size in bytes in the original.</summary>
    public const int Size = 848;

    /// <summary>Gets a value indicating whether the alarm is sounding this tick — <c>+673</c>.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Gets a value indicating whether it was sounding last tick — <c>+674</c>.</summary>
    public bool WasOn { get; private set; }

    /// <summary>Gets a value indicating whether the trigger condition held — <c>+672</c>.</summary>
    public bool IsTriggered { get; private set; }

    /// <summary>Gets the tick <see cref="IsTriggered"/> was last true on — <c>+676</c>.</summary>
    public int LastTriggeredTick { get; private set; }

    /// <summary>Gets the event an AI watching this control should be posted, if any.</summary>
    /// <remarks>
    /// <see cref="AiEvent.AlarmOn"/> every tick it is on; <see cref="AiEvent.AlarmOff"/> on the single tick
    /// after it stops; nothing otherwise.
    /// </remarks>
    public AiEvent? PendingEvent =>
        IsOn ? AiEvent.AlarmOn
        : WasOn ? AiEvent.AlarmOff
        : null;

    /// <summary>Gets a value indicating whether an AI watching this control should latch alert.</summary>
    /// <remarks>Only the on state sets it — the falling edge does not.</remarks>
    public bool ForcesAlert => IsOn;

    /// <summary>Advances one tick, as <c>0x4408A0</c> does.</summary>
    /// <param name="tick">The current tick counter.</param>
    /// <param name="disabled">The gate at <c>+400</c>.</param>
    /// <param name="enabled">The gate at <c>+576</c>; the update requires it.</param>
    /// <param name="triggerCondition">
    /// The condition at <c>+684</c>, or <see langword="null"/> when the parameter is absent — in which case the
    /// flag <b>keeps its previous value</b> rather than clearing, because the original only assigns when the
    /// expression exists.
    /// </param>
    /// <param name="onCondition">The condition at <c>+764</c>, with the same absent-means-keep rule.</param>
    public void Update(int tick, bool disabled, bool enabled, bool? triggerCondition, bool? onCondition)
        => UpdateWithAlarmExpression(tick, disabled, enabled, triggerCondition, () => onCondition);

    /// <summary>
    /// Advances one tick while deferring the alarm expression until after <see cref="IsTriggered"/> has
    /// been written. Retail evaluates in that order, so an alarm expression may read this tick's trigger.
    /// </summary>
    public void UpdateWithAlarmExpression(
        int tick,
        bool disabled,
        bool enabled,
        bool? triggerCondition,
        Func<bool?> onCondition)
    {
        ArgumentNullException.ThrowIfNull(onCondition);

        // The shadow is taken FIRST, before the new state is computed.
        WasOn = IsOn;

        if (disabled || !enabled)
        {
            IsTriggered = false;
            IsOn = false;
            return;
        }

        if (triggerCondition is bool triggered)
        {
            IsTriggered = triggered;
        }

        if (IsTriggered)
        {
            LastTriggeredTick = tick;
        }

        if (onCondition() is bool on)
        {
            IsOn = on;
        }
    }
}
