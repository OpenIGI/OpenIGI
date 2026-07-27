namespace OpenIGI.Input;

/// <summary>
/// The 256-key state model of the original, with the previous frame's shadow that makes edge detection
/// exact.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps three 256-byte arrays: the current state, the previous frame's copy, and a
/// separate array written by the window-message hook when the game runs with <c>-WinInput</c>. A key
/// is down when its byte has the high bit set. <see cref="Update"/> reproduces
/// <c>Input_Poll</c> (<c>0x490230</c>) exactly, including the ordering that matters: <b>the shadow is
/// taken before the new state is read, unconditionally, every frame</b> — so a frame where the device
/// could not be read reports every key as released with a correct falling edge rather than a stuck
/// key.
/// </para>
/// <para>
/// Indices are DirectInput scan codes; see <see cref="KeyCode"/>.
/// </para>
/// </remarks>
public sealed class KeyboardState
{
    /// <summary>The number of keys tracked, matching the original's array size.</summary>
    public const int KeyCount = 256;

    /// <summary>The byte value the original writes for a key that is down.</summary>
    public const byte DownValue = 0x80;

    private readonly byte[] _current = new byte[KeyCount];
    private readonly byte[] _previous = new byte[KeyCount];

    /// <summary>Gets the current state bytes, one per scan code.</summary>
    public ReadOnlySpan<byte> Current => _current;

    /// <summary>Gets the previous frame's state bytes, one per scan code.</summary>
    public ReadOnlySpan<byte> Previous => _previous;

    /// <summary>Takes the frame shadow and replaces the current state.</summary>
    /// <param name="keys">The new state, one byte per scan code, high bit set for down. Shorter spans leave the remaining keys released.</param>
    /// <exception cref="ArgumentException"><paramref name="keys"/> is longer than <see cref="KeyCount"/>.</exception>
    public void Update(ReadOnlySpan<byte> keys)
    {
        if (keys.Length > KeyCount)
        {
            throw new ArgumentException($"A keyboard state has at most {KeyCount} keys, got {keys.Length}.", nameof(keys));
        }

        // Shadow first, then overwrite — the order in Input_Poll.
        _current.AsSpan().CopyTo(_previous);
        _current.AsSpan().Clear();
        keys.CopyTo(_current);
    }

    /// <summary>Takes the frame shadow and clears every key, as the original does when the device cannot be read.</summary>
    public void Release()
    {
        _current.AsSpan().CopyTo(_previous);
        _current.AsSpan().Clear();
    }

    /// <summary>Sets one key's state without taking a shadow. For hosts that receive key events rather than polling a device.</summary>
    /// <param name="key">The key.</param>
    /// <param name="down"><see langword="true"/> when the key is down.</param>
    /// <remarks>Pair with <see cref="BeginFrame"/>, which takes the shadow once per frame.</remarks>
    public void SetKey(KeyCode key, bool down) => _current[(byte)key] = down ? DownValue : (byte)0;

    /// <summary>Takes the frame shadow, leaving the current state in place.</summary>
    /// <remarks>
    /// The event-driven counterpart to <see cref="Update"/>: call once per frame before the host
    /// dispatches that frame's key events through <see cref="SetKey"/>.
    /// </remarks>
    public void BeginFrame() => _current.AsSpan().CopyTo(_previous);

    /// <summary>Determines whether a key is down this frame.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the key is down.</returns>
    public bool IsDown(KeyCode key) => (_current[(byte)key] & DownValue) != 0;

    /// <summary>Determines whether a key is up this frame.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the key is up.</returns>
    public bool IsUp(KeyCode key) => (_current[(byte)key] & DownValue) == 0;

    /// <summary>Determines whether a key went down between the previous frame and this one.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> on the frame the key is first down.</returns>
    public bool WasPressed(KeyCode key) =>
        (_current[(byte)key] & DownValue) != 0 && (_previous[(byte)key] & DownValue) == 0;

    /// <summary>Determines whether a key went up between the previous frame and this one.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> on the frame the key is first up again.</returns>
    public bool WasReleased(KeyCode key) =>
        (_current[(byte)key] & DownValue) == 0 && (_previous[(byte)key] & DownValue) != 0;

    /// <summary>Clears both the current state and the shadow.</summary>
    public void Reset()
    {
        _current.AsSpan().Clear();
        _previous.AsSpan().Clear();
    }
}
