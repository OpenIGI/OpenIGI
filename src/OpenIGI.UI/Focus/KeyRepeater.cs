using OpenIGI.Input;

namespace OpenIGI.UI.Focus;

/// <summary>
/// Turns a held key into a repeating stream of presses.
/// </summary>
/// <remarks>
/// <para>
/// The input layer reports edges, not repeats: <see cref="KeyboardState.WasPressed"/> is true for
/// exactly one frame however long a key is held, and the platform's own character repeat is not
/// something the engine relies on. That is correct for a game, where a held key is a state rather than
/// a stream, and quite wrong for text editing — holding backspace should erase a line, not one
/// character.
/// </para>
/// <para>
/// The delay before the first repeat is longer than the interval between later ones, which is what
/// stops an ordinary single press from turning into two. The values here are faster than the menu's
/// navigation repeat because editing text wants a quicker cadence than moving a selection does.
/// </para>
/// <para>
/// One instance tracks one key. A caller keeps a few — backspace, delete, the arrows — and polls each
/// one per frame.
/// </para>
/// </remarks>
public sealed class KeyRepeater
{
    /// <summary>How long a key must be held before it starts repeating, in seconds.</summary>
    public const float DefaultDelay = 0.40f;

    /// <summary>How long between repeats once they have started, in seconds.</summary>
    public const float DefaultInterval = 0.035f;

    private float _held;
    private float _until;
    private bool _repeating;

    /// <summary>Initializes a new instance of the <see cref="KeyRepeater"/> class.</summary>
    /// <param name="delay">How long the key must be held before repeating.</param>
    /// <param name="interval">How long between repeats.</param>
    public KeyRepeater(float delay = DefaultDelay, float interval = DefaultInterval)
    {
        Delay = MathF.Max(0f, delay);
        Interval = MathF.Max(0.001f, interval);
    }

    /// <summary>Gets how long the key must be held before repeating, in seconds.</summary>
    public float Delay { get; }

    /// <summary>Gets how long between repeats, in seconds.</summary>
    public float Interval { get; }

    /// <summary>Advances the repeater and reports whether the key should act this frame.</summary>
    /// <param name="keyboard">The frame's keyboard state.</param>
    /// <param name="key">The key being tracked.</param>
    /// <param name="elapsedSeconds">Real seconds since the last poll.</param>
    /// <returns><see langword="true"/> when the key was pressed this frame or has repeated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyboard"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Returns true on the pressing edge as well, so a caller replaces a bare <c>WasPressed</c> test
    /// with this and gets repeat for free rather than handling the two cases separately.
    /// </remarks>
    public bool Poll(KeyboardState keyboard, KeyCode key, float elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(keyboard);

        if (keyboard.WasPressed(key))
        {
            _held = 0f;
            _until = Delay;
            _repeating = true;
            return true;
        }

        if (!_repeating || !keyboard.IsDown(key))
        {
            Reset();
            return false;
        }

        _held += elapsedSeconds;
        if (_held < _until)
        {
            return false;
        }

        _until += Interval;
        return true;
    }

    /// <summary>Forgets the key is held, so the next press starts a fresh delay.</summary>
    /// <remarks>Called when focus moves away, so a key still down elsewhere does not resume repeating here.</remarks>
    public void Reset()
    {
        _held = 0f;
        _until = Delay;
        _repeating = false;
    }
}
