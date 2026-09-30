namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Runs an <c>AnimData</c> programme — the scripted channel timeline an <c>AnimTask</c> plays into
/// another task.
/// </summary>
/// <remarks>
/// <para>
/// The programme is a flat list of <see cref="AnimDataEntry"/>, and running it is simpler than it
/// looks: <b>an entry just writes its value to the channel its opcode names.</b> There is no
/// instruction set. Two of the 34 channels are the interpreter's own state, which is what makes the
/// stream look like it has control flow:
/// </para>
/// <list type="bullet">
/// <item><description>
/// channel <see cref="WaitChannel"/> is the <b>remaining delay in ticks</b> — the step loop keeps
/// consuming entries while it reads zero, so writing it is what pauses the programme;
/// </description></item>
/// <item><description>
/// channel <see cref="ProgramCounterChannel"/> is the <b>program counter</b> itself, so an entry
/// naming it would be a jump. No retail programme does.
/// </description></item>
/// </list>
/// <para>
/// Both are stored as <see cref="float"/> like every other channel, and the delay is truncated to an
/// integer on the way in — twice, once when the script is parsed and again on execution.
/// </para>
/// </remarks>
public sealed class AnimDataProgram
{
    /// <summary>The number of channels — the size of the state block the original memsets.</summary>
    public const int ChannelCount = 34;

    /// <summary>The channel holding the remaining delay in ticks.</summary>
    public const int WaitChannel = 32;

    /// <summary>The channel holding the program counter.</summary>
    public const int ProgramCounterChannel = 33;

    private readonly AnimDataEntry[] _entries;
    private readonly float[] _channels = new float[ChannelCount];

    /// <summary>Initializes a new instance of the <see cref="AnimDataProgram"/> class.</summary>
    /// <param name="entries">The programme, as parsed from the script.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An opcode is not a channel.</exception>
    public AnimDataProgram(IReadOnlyList<AnimDataEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = [.. entries];
        foreach (AnimDataEntry entry in _entries)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(entry.Code, nameof(entries));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entry.Code, ChannelCount, nameof(entries));
        }
    }

    /// <summary>Gets the programme's entries.</summary>
    public IReadOnlyList<AnimDataEntry> Entries => _entries;

    /// <summary>Gets the channel values, which are what the programme publishes to its target.</summary>
    public ReadOnlySpan<float> Channels => _channels;

    /// <summary>Writes a channel value on behalf of the task receiving this programme.</summary>
    /// <param name="channel">The channel to update.</param>
    /// <param name="value">The value published on subsequent ticks.</param>
    /// <remarks>
    /// Retail sends the live channel block by pointer. Vehicle handlers use that alias on tick zero to
    /// install their hold/reset sentinels, so the receiver can legitimately change later input.
    /// </remarks>
    public void WriteChannel(int channel, float value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, ChannelCount);
        _channels[channel] = value;
    }

    /// <summary>Gets the tick the programme has been advanced to; <c>-1</c> before it first runs.</summary>
    /// <remarks>The constructor starts a task's counter at <c>-1</c>, so its first update steps to 0.</remarks>
    public int Tick { get; private set; } = -1;

    /// <summary>Gets the program counter — the same value as channel <see cref="ProgramCounterChannel"/>.</summary>
    public int ProgramCounter => (int)_channels[ProgramCounterChannel];

    /// <summary>Gets the remaining delay — the same value as channel <see cref="WaitChannel"/>.</summary>
    public int RemainingWait => (int)_channels[WaitChannel];

    /// <summary>Gets whether the program counter has run off the end.</summary>
    public bool HasFinished => ProgramCounter >= _entries.Length;

    /// <summary>Runs one tick.</summary>
    /// <remarks>
    /// Reproduces <c>0x4F3F60</c>: spend a tick of the pending delay, then consume entries until one
    /// writes a non-zero delay or the programme runs out.
    /// </remarks>
    public void Step()
    {
        if (ProgramCounter >= _entries.Length)
        {
            return;
        }

        if (_channels[WaitChannel] != 0f)
        {
            _channels[WaitChannel] -= 1f;
            if (_channels[WaitChannel] == 0f)
            {
                _channels[ProgramCounterChannel] += 1f;
            }
        }

        while (_channels[WaitChannel] == 0f)
        {
            int pc = ProgramCounter;
            if (pc >= _entries.Length)
            {
                break;
            }

            AnimDataEntry entry = _entries[pc];
            float value = entry.Code == WaitChannel ? MathF.Truncate(entry.Value) : entry.Value;
            _channels[entry.Code] = value;

            // An entry that wrote the program counter has already moved it; only advance when the
            // delay is still zero, which is also what stops the loop after a wait is written.
            if (_channels[WaitChannel] == 0f)
            {
                _channels[ProgramCounterChannel] += 1f;
            }
        }
    }

    /// <summary>Moves the tick counter on once a step's packet has gone out.</summary>
    /// <remarks>
    /// <c>0x4F3EF0</c> calls the step <c>0x4F3F60</c>, whose packet (<c>0x4F4060</c>) carries the counter
    /// <c>+131412</c> as it stands, and only then increments it. A running AnimTask therefore steps,
    /// sends, and counts, in that order.
    /// </remarks>
    public void CountTick() => Tick++;

    /// <summary>Advances the programme to a tick.</summary>
    /// <param name="tick">The tick to reach.</param>
    /// <remarks>
    /// Reproduces <c>0x4F3EF0</c>. Seeking forward steps; seeking to zero or backwards <b>clears the
    /// channels and replays from the start</b>, because the interpreter cannot run in reverse. That
    /// makes a seek deterministic but costs time proportional to the target tick.
    /// </remarks>
    public void SeekTo(int tick)
    {
        if (tick != 0 && tick >= Tick)
        {
            while (tick > Tick)
            {
                Step();
                Tick++;
            }

            return;
        }

        Array.Clear(_channels);
        Tick = 0;
        for (int i = 0; i <= tick; i++)
        {
            Step();
        }
    }

    /// <summary>Sums the delays the programme writes, giving its length in ticks.</summary>
    /// <returns>The total, which for every retail programme is its full duration.</returns>
    /// <remarks>
    /// Exact only because every retail programme ends with a delay and none writes the program
    /// counter; a programme that jumped would not have a length this simple.
    /// </remarks>
    public int TotalTicks()
    {
        int total = 0;
        foreach (AnimDataEntry entry in _entries)
        {
            if (entry.Code == WaitChannel)
            {
                total += (int)MathF.Truncate(entry.Value);
            }
        }

        return total;
    }
}
