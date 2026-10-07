namespace OpenIGI.Game.Igi2.Humans;

/// <summary>A human's channel block: this tick's 63 control values and last tick's.</summary>
/// <remarks>
/// <para>
/// <c>Human_s.avChannel</c> and <c>avLastChannel</c>. Whatever drives the human — the player's input task
/// or an AI — hands it a full frame once a tick through the <c>HumanUpdate</c> event, and the handler
/// (<c>HumanPlayer_HumanUpdateHandler</c>, <c>igi2 0x4B9A80</c>) copies the current frame to the last one
/// before taking the new one, unless the channels are locked. Every "pressed this tick" test the state
/// machines make is that pair: non-zero now and zero last tick.
/// </para>
/// </remarks>
public sealed class Igi2HumanChannels
{
    /// <summary>The number of channels: 252 bytes of floats.</summary>
    public const int Count = 63;

    private readonly float[] _current = new float[Count];
    private readonly float[] _last = new float[Count];

    /// <summary>Gets or sets a value indicating whether new frames are refused (<c>Human_s.isLockChannel</c>).</summary>
    public bool IsLocked { get; set; }

    /// <summary>Gets or sets one of this tick's channels.</summary>
    /// <param name="channel">The channel.</param>
    /// <remarks>
    /// Writable because the human's own update clears channels it has consumed or forbidden this tick —
    /// fire while a computer page is up, the activate channel while activation is locked.
    /// </remarks>
    public float this[Igi2HumanChannel channel]
    {
        get => _current[(int)channel];
        set => _current[(int)channel] = value;
    }

    /// <summary>Gets this tick's frame.</summary>
    public ReadOnlySpan<float> Current => _current;

    /// <summary>Gets last tick's frame.</summary>
    public ReadOnlySpan<float> Last => _last;

    /// <summary>Takes the next frame, keeping this one as the last.</summary>
    /// <param name="frame">The new frame, <see cref="Count"/> values.</param>
    /// <exception cref="ArgumentException">The frame is not <see cref="Count"/> values.</exception>
    /// <remarks>A locked block keeps both frames as they are, so nothing reads as newly pressed.</remarks>
    public void Accept(ReadOnlySpan<float> frame)
    {
        if (frame.Length != Count)
        {
            throw new ArgumentException($"A channel frame is {Count} values, not {frame.Length}.", nameof(frame));
        }

        if (IsLocked)
        {
            return;
        }

        _current.CopyTo(_last, 0);
        frame.CopyTo(_current);
    }

    /// <summary>Gets a value indicating whether a channel is non-zero this tick.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns><see langword="true"/> while it is held.</returns>
    public bool IsHeld(Igi2HumanChannel channel) => _current[(int)channel] != 0f;

    /// <summary>Gets a value indicating whether a channel became non-zero this tick.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns><see langword="true"/> on the tick it is pressed.</returns>
    public bool WasPressed(Igi2HumanChannel channel) =>
        _current[(int)channel] != 0f && _last[(int)channel] == 0f;
}
