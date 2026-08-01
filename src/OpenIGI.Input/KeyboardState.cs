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

    /// <summary>The most presses the queue holds before it starts dropping them.</summary>
    /// <remarks>
    /// The original's event queue is a fixed ring, and a full one refuses further writes rather than
    /// growing. The cap matters here for the same reason it does there: a consumer that stops draining
    /// — a level load, a stalled frame — must not be able to make the queue grow without bound.
    /// </remarks>
    public const int PressQueueCapacity = 64;

    private readonly byte[] _current = new byte[KeyCount];
    private readonly byte[] _previous = new byte[KeyCount];
    private readonly Queue<KeyCode> _presses = new();
    private readonly Queue<char> _characters = new();

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

        for (int i = 0; i < keys.Length; i++)
        {
            if ((_current[i] & DownValue) != 0 && (_previous[i] & DownValue) == 0)
            {
                EnqueuePress((KeyCode)i);
            }
        }
    }

    /// <summary>Takes the frame shadow and clears every key, as the original does when the device cannot be read.</summary>
    public void Release()
    {
        Captured = false;
        _current.AsSpan().CopyTo(_previous);
        _current.AsSpan().Clear();
        _presses.Clear();
        _characters.Clear();
    }

    /// <summary>Sets one key's state without taking a shadow. For hosts that receive key events rather than polling a device.</summary>
    /// <param name="key">The key.</param>
    /// <param name="down"><see langword="true"/> when the key is down.</param>
    /// <remarks>
    /// Pair with <see cref="BeginFrame"/>, which takes the shadow once per frame. A rising edge is also
    /// queued — see <see cref="TryDequeuePress"/> — because a consumer running slower than the host's
    /// event dispatch would otherwise never see it.
    /// </remarks>
    public void SetKey(KeyCode key, bool down)
    {
        bool wasDown = (_current[(byte)key] & DownValue) != 0;
        _current[(byte)key] = down ? DownValue : (byte)0;

        if (down && !wasDown)
        {
            EnqueuePress(key);
        }
    }

    /// <summary>Gets or sets a value indicating whether the shell has taken this frame's keyboard input.</summary>
    /// <remarks>
    /// While set, every read reports nothing but the underlying state is left intact — see
    /// <c>InputState.Captured</c>. The capturing code sets this once it has read what it needs, so the
    /// developer console can type without the player also walking.
    /// </remarks>
    public bool Captured { get; set; }

    /// <summary>Gets the number of presses waiting to be read.</summary>
    public int PendingPressCount => Captured ? 0 : _presses.Count;

    /// <summary>Takes the oldest press that has not been read yet.</summary>
    /// <param name="key">Receives the key.</param>
    /// <returns><see langword="false"/> when nothing is waiting.</returns>
    /// <remarks>
    /// <para>
    /// This is the original's input event queue — <c>Input_HasEvent</c> (<c>0x48A910</c>) and
    /// <c>Input_NextEvent</c> (<c>0x48A8D0</c>), which <c>MenuManager_HandleKeys</c> drains in a
    /// <c>while</c> loop. It is not an alternative spelling of <see cref="WasPressed"/>.
    /// </para>
    /// <para>
    /// The difference is only visible when the reader and the host run at different rates, which is
    /// exactly the menu's situation: presses are dispatched at the display's rate and read at the
    /// simulation's 30 Hz. <see cref="WasPressed"/> compares against a shadow taken every frame, so a
    /// press between two ticks is overwritten before any tick sees it — the queue keeps it.
    /// </para>
    /// </remarks>
    public bool TryDequeuePress(out KeyCode key)
    {
        if (Captured || _presses.Count == 0)
        {
            key = default;
            return false;
        }

        key = _presses.Dequeue();
        return true;
    }

    /// <summary>Records a typed character.</summary>
    /// <param name="character">The character the platform decoded.</param>
    /// <remarks>
    /// Separate from the press queue because a key and a character are different things: the same
    /// press yields <c>KEY_A</c> and either <c>a</c> or <c>A</c> depending on modifiers and layout,
    /// and only the platform knows which. A text field wants the character.
    /// </remarks>
    public void EnqueueCharacter(char character)
    {
        if (_characters.Count < PressQueueCapacity)
        {
            _characters.Enqueue(character);
        }
    }

    /// <summary>Takes the oldest typed character that has not been read yet.</summary>
    /// <param name="character">Receives the character.</param>
    /// <returns><see langword="false"/> when nothing is waiting.</returns>
    public bool TryDequeueCharacter(out char character)
    {
        if (Captured || _characters.Count == 0)
        {
            character = default;
            return false;
        }

        character = _characters.Dequeue();
        return true;
    }

    private void EnqueuePress(KeyCode key)
    {
        if (_presses.Count < PressQueueCapacity)
        {
            _presses.Enqueue(key);
        }
    }

    /// <summary>Takes the frame shadow, leaving the current state in place.</summary>
    /// <remarks>
    /// The event-driven counterpart to <see cref="Update"/>: call once per frame before the host
    /// dispatches that frame's key events through <see cref="SetKey"/>.
    /// </remarks>
    public void BeginFrame()
    {
        Captured = false;
        _current.AsSpan().CopyTo(_previous);
    }

    /// <summary>Determines whether a key is down this frame.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the key is down.</returns>
    public bool IsDown(KeyCode key) => !Captured && (_current[(byte)key] & DownValue) != 0;

    /// <summary>Determines whether a key is up this frame.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the key is up.</returns>
    public bool IsUp(KeyCode key) => Captured || (_current[(byte)key] & DownValue) == 0;

    /// <summary>Determines whether a key went down between the previous frame and this one.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> on the frame the key is first down.</returns>
    public bool WasPressed(KeyCode key) =>
        !Captured && (_current[(byte)key] & DownValue) != 0 && (_previous[(byte)key] & DownValue) == 0;

    /// <summary>Determines whether a key went up between the previous frame and this one.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> on the frame the key is first up again.</returns>
    public bool WasReleased(KeyCode key) =>
        !Captured && (_current[(byte)key] & DownValue) == 0 && (_previous[(byte)key] & DownValue) != 0;

    /// <summary>Clears both the current state and the shadow.</summary>
    public void Reset()
    {
        _presses.Clear();
        _characters.Clear();
        _current.AsSpan().Clear();
        _previous.AsSpan().Clear();
    }
}
