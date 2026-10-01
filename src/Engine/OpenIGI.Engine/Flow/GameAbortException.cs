namespace OpenIGI.Engine.Flow;

/// <summary>
/// Control-flow signal that abandons the rest of the current frame.
/// </summary>
/// <remarks>
/// <para>
/// Models the original's <c>Game_Abort</c> (<c>0x4010A0</c>), a <c>longjmp</c> back to the
/// <c>setjmp</c> at the top of <c>Game_Frame</c> (<c>0x401050</c>). Despite what the earlier notes say,
/// it is not a level-restart mechanism: it has exactly one call site, the <see cref="FlowEvent.Quit"/>
/// arm of the flow switch, and level reloads never unwind at all.
/// </para>
/// <para>
/// What it observably skips is the tail of the frame after the flow switch — publishing the tick count
/// and advancing the time base. Nothing else, because every tree walk has already returned by then.
/// That skipping is behaviour, not a bug to fix: see the original.
/// </para>
/// <para>
/// Caught at the frame boundary by <see cref="GameLoop.RunFrame"/>. It is not an error, so it does not
/// derive from <see cref="OpenIGI.Core.Diagnostics.EngineFatalException"/>.
/// </para>
/// </remarks>
public sealed class GameAbortException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="GameAbortException"/> class.</summary>
    public GameAbortException()
        : this(FlowEvent.Quit)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GameAbortException"/> class.</summary>
    /// <param name="cause">The flow event that triggered the abort.</param>
    public GameAbortException(FlowEvent cause)
        : base($"The frame was aborted by the flow event {cause}.") => Cause = cause;

    /// <summary>Initializes a new instance of the <see cref="GameAbortException"/> class.</summary>
    /// <param name="message">The message.</param>
    public GameAbortException(string message)
        : base(message) => Cause = FlowEvent.Quit;

    /// <summary>Initializes a new instance of the <see cref="GameAbortException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public GameAbortException(string message, Exception innerException)
        : base(message, innerException) => Cause = FlowEvent.Quit;

    /// <summary>Gets the flow event that abandoned the frame.</summary>
    public FlowEvent Cause { get; }
}
