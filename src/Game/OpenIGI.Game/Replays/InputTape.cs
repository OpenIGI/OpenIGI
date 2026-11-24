using OpenIGI.Game.Player;

namespace OpenIGI.Game.Replays;

/// <summary>
/// A level as it was played: the input its player was given on each tick it ran, the ticks its cutscene was being broken, the
/// commands that changed the game and the tick each came before, the level's fingerprint every so often, and marks for a
/// scrubber. The level runs the same from the same start given the same of these, so this is all a replay is.
/// </summary>
/// <remarks>
/// Ticks are the level's own (<c>GameHost.TickOfLevel</c>): the ticks it ran since it loaded, not frames, and not ticks a menu
/// held it through. A tick the player did not tick on -- a cutscene, a death -- holds no input.
/// </remarks>
public sealed class InputTape
{
    /// <summary>The most ticks a tape holds: two hours of play.</summary>
    public const int MaxTicks = 2 * 60 * 60 * 30;

    /// <summary>The most commands, marks or fingerprints a tape holds of each.</summary>
    public const int MaxEntries = 100_000;

    private readonly List<TitlePlayerInput?> _inputs = [];
    private readonly List<bool> _breaks = [];
    private readonly List<(int Tick, string Command)> _commands = [];
    private readonly List<(int Tick, string Text)> _marks = [];
    private readonly List<(int Tick, ulong Digest)> _digests = [];

    /// <summary>Gets how many ticks it covers: the ticks the level ran while it recorded, not frames that only looked at one.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the commands, each with the tick it came before, in the order given.</summary>
    public IReadOnlyList<(int Tick, string Command)> Commands => _commands;

    /// <summary>Gets the marks, each with its tick, in the order made.</summary>
    public IReadOnlyList<(int Tick, string Text)> Marks => _marks;

    /// <summary>Gets the level's fingerprints, each after the tick it is of, in tick order.</summary>
    public IReadOnlyList<(int Tick, ulong Digest)> Digests => _digests;

    /// <summary>Gets a value indicating whether it is full: <see cref="MaxTicks"/> ticks, or <see cref="MaxEntries"/> of anything.</summary>
    public bool IsFull => Length >= MaxTicks || _commands.Count >= MaxEntries || _marks.Count >= MaxEntries || _digests.Count >= MaxEntries;

    /// <summary>Notes a tick the level ran.</summary>
    /// <param name="tick">The tick.</param>
    public void Ran(int tick)
    {
        if (tick >= 0 && tick < MaxTicks)
        {
            Length = Math.Max(Length, tick + 1);
        }
    }

    /// <summary>Records the input a tick was given.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="input">The input.</param>
    public void SetInput(int tick, in TitlePlayerInput input)
    {
        if (Grow(tick))
        {
            _inputs[tick] = input;
        }
    }

    /// <summary>Records whether the cutscene was being broken as a tick began.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="breaking">Whether it was.</param>
    public void SetBreak(int tick, bool breaking)
    {
        if (Grow(tick))
        {
            _breaks[tick] = breaking;
        }
    }

    /// <summary>Records a command that changes the game, before the tick it came before.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="command">The command.</param>
    public void Command(int tick, string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_commands.Count < MaxEntries)
        {
            _commands.Add((tick, command));
        }
    }

    /// <summary>Marks a tick for a scrubber.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="text">What the mark says.</param>
    public void Mark(int tick, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_marks.Count < MaxEntries)
        {
            _marks.Add((tick, text));
        }
    }

    /// <summary>Records the level's fingerprint after a tick.</summary>
    /// <param name="tick">The tick it is of.</param>
    /// <param name="digest">The fingerprint (<see cref="SimulationHash"/>).</param>
    public void SetDigest(int tick, ulong digest)
    {
        if (_digests.Count < MaxEntries && (_digests.Count == 0 || _digests[^1].Tick < tick))
        {
            _digests.Add((tick, digest));
        }
    }

    /// <summary>The input a tick was given.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The input, or <see langword="null"/> for a tick that took none, or one the tape does not reach.</returns>
    public TitlePlayerInput? InputAt(int tick) => tick >= 0 && tick < _inputs.Count ? _inputs[tick] : null;

    /// <summary>Whether the cutscene was being broken as a tick began.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Whether it was.</returns>
    public bool BreaksAt(int tick) => tick >= 0 && tick < _breaks.Count && _breaks[tick];

    /// <summary>The commands given before a tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Them, in the order given.</returns>
    public IEnumerable<string> CommandsAt(int tick) => _commands.Where(c => c.Tick == tick).Select(static c => c.Command);

    /// <summary>The commands given before any tick before this one.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Them, in the order given.</returns>
    public IEnumerable<string> CommandsBefore(int tick) => _commands.Where(c => c.Tick < tick).Select(static c => c.Command);

    /// <summary>The level's fingerprint recorded after a tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The fingerprint, or <see langword="null"/> where none was recorded.</returns>
    public ulong? DigestAt(int tick)
    {
        int lo = 0;
        int hi = _digests.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int at = _digests[mid].Tick;
            if (at == tick)
            {
                return _digests[mid].Digest;
            }

            if (at < tick)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return null;
    }

    // Room for a tick, within the tape's reach.
    private bool Grow(int tick)
    {
        if (tick < 0 || tick >= MaxTicks)
        {
            return false;
        }

        while (_inputs.Count <= tick)
        {
            _inputs.Add(null);
            _breaks.Add(false);
        }

        return true;
    }
}
