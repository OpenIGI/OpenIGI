namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A boolean task field paired with the previous tick's value, so scripts can detect edges.
/// </summary>
/// <remarks>
/// <para>
/// Every latched boolean in retail level data has a <c>Last</c>-prefixed shadow —
/// <c>isPressed</c>/<c>isLastPressed</c>, <c>isInUse</c>/<c>isLastInUse</c> — because mission scripts
/// have no memory between ticks and need to ask "did this just become true?". The owning task copies
/// the value into the shadow at the end of its own update; both are exported as live script variables.
/// </para>
/// <para>
/// Confirmed in <c>location0/level1</c>: <c>Switch_94.isLastPressed</c>, <c>Switch_92.isLastPressed</c>.
/// </para>
/// </remarks>
public sealed class ScriptFlag
{
    /// <summary>Initializes a new instance of the <see cref="ScriptFlag"/> class.</summary>
    /// <param name="value">The initial value, which also seeds the shadow.</param>
    public ScriptFlag(bool value = false)
    {
        Value = value;
        Last = value;
    }

    /// <summary>Gets or sets the current value.</summary>
    public bool Value { get; set; }

    /// <summary>Gets the value as of the end of the previous tick.</summary>
    public bool Last { get; private set; }

    /// <summary>Gets a value indicating whether the flag became true this tick.</summary>
    public bool IsRisingEdge => Value && !Last;

    /// <summary>Gets a value indicating whether the flag became false this tick.</summary>
    public bool IsFallingEdge => !Value && Last;

    /// <summary>Copies the current value into the shadow. Call at the end of the owner's update.</summary>
    public void CommitTick() => Last = Value;

    /// <inheritdoc/>
    public override string ToString() => $"{Value} (was {Last})";
}
