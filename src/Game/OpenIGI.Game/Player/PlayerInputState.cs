namespace OpenIGI.Game.Player;

/// <summary>
/// The player's pair of input frames — the newest and the one before it — and the edge tests over them.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanPlayer</c>'s handler for the input message (<c>0x410960</c>) is three lines: copy current to
/// previous, copy the incoming frame to current, then run the update. The pair lives at <c>+432</c>
/// (current) and <c>+272</c> (previous), and every edge-triggered action in the update is
/// <c>current != 0 &amp;&amp; previous == 0</c>.
/// </para>
/// <para>
/// The copy is skipped entirely while <see cref="IsFrozen"/> is set (<c>HumanPlayer+592</c>), which
/// holds both frames still rather than clearing them — so an action already held stays held and no edge
/// fires until input resumes.
/// </para>
/// </remarks>
public sealed class PlayerInputState
{
    /// <summary>Gets the newest frame.</summary>
    public PlayerInputFrame Current { get; private set; }

    /// <summary>Gets the frame before <see cref="Current"/>.</summary>
    public PlayerInputFrame Previous { get; private set; }

    /// <summary>Gets or sets whether incoming frames are ignored.</summary>
    /// <remarks>The original's <c>HumanPlayer+592</c>. Both frames are left as they stand.</remarks>
    public bool IsFrozen { get; set; }

    /// <summary>Takes delivery of a sampled frame.</summary>
    /// <param name="frame">The frame the input task posted.</param>
    /// <returns>
    /// <see langword="true"/> when the frame was accepted, <see langword="false"/> when
    /// <see cref="IsFrozen"/> discarded it.
    /// </returns>
    public bool Accept(PlayerInputFrame frame)
    {
        if (IsFrozen)
        {
            return false;
        }

        Previous = Current;
        Current = frame;
        return true;
    }

    /// <summary>Reports whether a slot is engaged this tick.</summary>
    /// <param name="slot">The slot to test.</param>
    /// <returns><see langword="true"/> when the current frame's slot is non-zero.</returns>
    public bool IsSet(PlayerInputSlot slot) => Current.IsSet(slot);

    /// <summary>Reports whether a slot became engaged this tick.</summary>
    /// <param name="slot">The slot to test.</param>
    /// <returns><see langword="true"/> when the slot is set now and was clear last tick.</returns>
    public bool IsRisingEdge(PlayerInputSlot slot) => Current.IsSet(slot) && !Previous.IsSet(slot);

    /// <summary>Reports whether a slot was released this tick.</summary>
    /// <param name="slot">The slot to test.</param>
    /// <returns><see langword="true"/> when the slot is clear now and was set last tick.</returns>
    public bool IsFallingEdge(PlayerInputSlot slot) => !Current.IsSet(slot) && Previous.IsSet(slot);

    /// <summary>Clears both frames.</summary>
    public void Reset()
    {
        Current = default;
        Previous = default;
    }
}
