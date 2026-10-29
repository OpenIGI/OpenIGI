namespace OpenIGI.Host.Services;

/// <summary>The hold that skips a cutscene: the break key held down long enough, with how far it has got for the ring that shows it.</summary>
/// <remarks>
/// <para>
/// The original skipped on the break key's first press, which a player reaching for the pause menu as a mission opens, or a
/// thumb landing on a phone's screen, set off by accident. Here the key has to be held for <see cref="HoldSeconds"/>; let go
/// early and the ring drains again. Both titles' cutscenes go through it: the host's own (Project I.G.I.'s level cutscenes)
/// break when <see cref="Update"/> reports the hold complete, and a title player's read <see cref="Breaking"/> as their
/// break key, held from then until the cutscene has ended, as the original's scripts expect to see it.
/// </para>
/// <para>
/// A key still held when one cutscene has been skipped does not go on to skip the next: it has to be let go first.
/// </para>
/// </remarks>
internal sealed class CutsceneSkipHold
{
    /// <summary>How long the key is held to skip.</summary>
    public const float HoldSeconds = 0.9f;

    /// <summary>How long the prompt stays up at a cutscene's start, and after the key is let go, before fading.</summary>
    public const float ShownSeconds = 3f;

    private const float FadeSeconds = 0.25f;
    private const float DrainRate = 3f;

    private bool _wasPlaying;
    private bool _mustRelease;
    private float _sinceActive;

    /// <summary>Gets how far the hold has got, from nought to one.</summary>
    public float Progress { get; private set; }

    /// <summary>Gets how visible the prompt is, from nought to one.</summary>
    public float Opacity { get; private set; }

    /// <summary>Gets whether the hold has completed and the cutscene playing is to break.</summary>
    public bool Breaking { get; private set; }

    /// <summary>Advances the hold by a frame.</summary>
    /// <param name="playing">Whether a cutscene the player could skip is playing.</param>
    /// <param name="held">Whether the break key is down.</param>
    /// <param name="seconds">How long the frame took.</param>
    /// <returns><see langword="true"/> on the one frame the hold completes.</returns>
    public bool Update(bool playing, bool held, float seconds)
    {
        seconds = Math.Max(0f, seconds);
        if (!playing)
        {
            _wasPlaying = false;
            Breaking = false;
            Progress = 0f;
            Opacity = Math.Max(0f, Opacity - (seconds / FadeSeconds));
            _mustRelease &= held;
            return false;
        }

        if (!_wasPlaying)
        {
            // A cutscene starting: the prompt is shown for a moment, so the player learns there is one.
            _wasPlaying = true;
            _sinceActive = 0f;
            _mustRelease = held;
        }

        _mustRelease &= held;
        bool holding = held && !_mustRelease && !Breaking;
        _sinceActive = holding || Breaking ? 0f : _sinceActive + seconds;
        bool shown = _sinceActive < ShownSeconds;
        Opacity = Math.Clamp(Opacity + ((shown ? 1f : -1f) * seconds / FadeSeconds), 0f, 1f);

        if (Breaking)
        {
            return false;
        }

        Progress = holding
            ? Math.Min(1f, Progress + (seconds / HoldSeconds))
            : Math.Max(0f, Progress - (seconds * DrainRate / HoldSeconds));
        if (Progress < 1f)
        {
            return false;
        }

        Breaking = true;
        _mustRelease = true;
        return true;
    }
}
