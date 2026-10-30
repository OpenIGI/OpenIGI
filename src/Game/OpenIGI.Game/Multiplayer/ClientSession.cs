using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>The client's side of a session: snapshots read and kept for drawing, and the player's commands sent up.</summary>
/// <remarks>
/// <para>
/// Each snapshot that arrives is read against the one it names (which this client acknowledged earlier), kept for the
/// next, handed to <see cref="Remote"/> for drawing the other humans, and used to set the clock. The next command
/// packet acknowledges it, so the server writes the next against it.
/// </para>
/// <para>
/// Commands go up once a tick, stamped with the tick the server should run them on (<see cref="TickClockSync.CommandTick"/>);
/// each packet repeats the newest <see cref="CommandRedundancy"/>, so a packet or two lost costs nothing. The commands sent
/// are kept in <see cref="SentCommands"/>, which prediction runs again over a correction from the server.
/// </para>
/// </remarks>
public sealed class ClientSession
{
    private const int KeptCommands = 64;

    private readonly NetClient _net;
    private readonly UserCommandCodec _codec;
    private readonly BitWriter _writer = new(512);
    private readonly List<UserCommand> _sent = [];
    private long _newestReceived = -1;
    private readonly Dictionary<long, Snapshot?[]> _parts = [];

    /// <summary>Initializes a new instance of the <see cref="ClientSession"/> class.</summary>
    /// <param name="net">The client's network end; the session reads its events, so nothing else should.</param>
    /// <param name="channelCount">The title's channel frame length.</param>
    public ClientSession(NetClient net, int channelCount)
    {
        _net = net ?? throw new ArgumentNullException(nameof(net));
        _codec = new UserCommandCodec(channelCount);
    }

    /// <summary>Gets the entity id of this client's own human, once welcomed.</summary>
    public int? EntityId { get; private set; }

    /// <summary>Gets the server's ticks per second, once welcomed.</summary>
    public int TickRate { get; private set; }

    /// <summary>Gets the map the server plays, once welcomed.</summary>
    public string? Map { get; private set; }

    /// <summary>Gets the estimate of the server's clock, once welcomed.</summary>
    public TickClockSync? Clock { get; private set; }

    /// <summary>Gets the snapshots that arrived, for drawing the other humans between them.</summary>
    public SnapshotInterpolator Remote { get; } = new(32);

    /// <summary>Gets the newest snapshot that arrived, or <see langword="null"/>.</summary>
    public Snapshot? Latest { get; private set; }

    /// <summary>Gets the newest scoreboard, or <see langword="null"/>.</summary>
    public Scoreboard? Scoreboard { get; private set; }

    /// <summary>Gets why the session ended or never started, or <see langword="null"/> while it goes on.</summary>
    public string? EndReason { get; private set; }

    /// <summary>Gets or sets how many of the newest commands each packet carries (<c>cl_cmdbackup</c>).</summary>
    public int CommandRedundancy { get; set; } = 3;

    /// <summary>Gets the newest commands sent, oldest first, for prediction to run again.</summary>
    public IReadOnlyList<UserCommand> SentCommands => _sent;

    /// <summary>Gets the newest tick whose command the server has run for this client, as of the newest snapshot.</summary>
    public long LastRunCommandTick { get; private set; } = long.MinValue;

    // How far behind the last stamp a command must be steered before ticks already sent are stamped again.
    private const int RestampTicks = 3;

    /// <summary>Gets how far ahead of the server's tick this client's newest command last arrived, in ticks.</summary>
    public int CommandLead { get; private set; }

    /// <summary>Gets what keeps this client's own player where the server has it.</summary>
    public PredictionReconciler Reconciler { get; } = new();

    /// <summary>Gets the snapshots that could not be read because their baseline was gone.</summary>
    public long DroppedSnapshots { get; private set; }

    private SnapshotHistory Received { get; } = new();

    /// <summary>Starts joining a server.</summary>
    /// <param name="server">The server.</param>
    /// <param name="request">The join request.</param>
    /// <param name="now">The current time in seconds.</param>
    public void Connect(NetAddress server, JoinRequest request, double now)
    {
        ArgumentNullException.ThrowIfNull(request);
        EndReason = null;
        _net.Connect(server, request.Encode(), now);
    }

    /// <summary>Reads what arrived.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Receive(double now)
    {
        _net.Update(now);
        while (_net.TryReceive(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Rejected or NetEventKind.Disconnected:
                    EndReason = e.Reason;
                    EntityId = null;
                    break;
                case NetEventKind.Message when e.Message.Data.Length > 0:
                    Read(e.Message.Data, now);
                    break;
            }
        }
    }

    /// <summary>Sends this tick's command, once welcomed and the clock is set.</summary>
    /// <param name="channels">The title's channel frame for this tick; copied.</param>
    /// <param name="now">The current time in seconds.</param>
    /// <returns>The tick the command was stamped for, or <see langword="null"/> when it was not sent.</returns>
    public long? SendCommand(ReadOnlySpan<float> channels, double now)
    {
        if (Clock is not { HasSample: true } clock || _net.Connection is not { } connection)
        {
            return null;
        }

        long tick = clock.CommandTick(now, connection.Statistics.RoundTripTime);
        if (_sent.Count > 0)
        {
            long last = _sent[^1].Tick;

            // Steered well behind what was sent (the lead was too long): stamp those ticks again from here, and stop
            // repeating the old ones, so the server runs this client's controls that much sooner (the ticks it already holds keep their first copy).
            if (tick <= last - RestampTicks)
            {
                _sent.RemoveAll(sent => sent.Tick >= tick);
                last = _sent.Count > 0 ? _sent[^1].Tick : tick - 1;
            }

            tick = Math.Max(tick, last + 1);

            // A tick skipped (a hitch on this side) is filled with the same controls, so the server's buffer has every tick.
            for (long missing = last + 1; missing < tick && missing <= last + UserCommandCodec.MaxCommandsPerPacket; missing++)
            {
                Keep(new UserCommand(missing, channels.ToArray()));
            }

            if (tick > last + UserCommandCodec.MaxCommandsPerPacket + 1)
            {
                _sent.Clear();
            }
        }

        Keep(new UserCommand(tick, channels.ToArray()));
        int count = Math.Clamp(CommandRedundancy, 1, UserCommandCodec.MaxCommandsPerPacket);
        int first = _sent.Count - 1;
        while (first > 0 && _sent.Count - first < count && _sent[first - 1].Tick == _sent[first].Tick - 1)
        {
            first--;
        }

        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Commands, 8);
        _writer.WriteBits((uint)_newestReceived, 32);
        _writer.WriteBits((uint)(_newestReceived >> 32), 32);
        _codec.Write(_writer, _sent.ToArray().AsSpan(first));
        _net.Send(NetChannel.Sequenced, _writer.AsSpan());
        return tick;
    }

    /// <summary>Sends what is queued.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Flush(double now) => _net.Flush(now);

    private void Keep(UserCommand command)
    {
        _sent.Add(command);
        if (_sent.Count > KeptCommands)
        {
            _sent.RemoveAt(0);
        }
    }

    // The parts of a split snapshot, gathered by tick: the whole once the last arrives, nothing before. A tick that never
    // completes is dropped when a newer one does, as a lost unsplit snapshot is.
    private Snapshot? Assemble(Snapshot piece, int part, int parts)
    {
        if (parts == 1)
        {
            return piece;
        }

        if (piece.Tick <= _newestReceived)
        {
            return null;
        }

        if (!_parts.TryGetValue(piece.Tick, out Snapshot?[]? gathered) || gathered.Length != parts)
        {
            gathered = new Snapshot?[parts];
            _parts[piece.Tick] = gathered;
        }

        gathered[part] = piece;
        if (gathered.Any(static p => p is null))
        {
            return null;
        }

        _parts.Remove(piece.Tick);
        foreach (long stale in _parts.Keys.Where(t => t < piece.Tick).ToArray())
        {
            _parts.Remove(stale);
        }

        return new Snapshot(piece.Tick, [.. gathered.SelectMany(static p => p!.Humans)]);
    }

    private void Read(byte[] data, double now)
    {
        BitReader reader = new(data);
        switch ((SessionMessage)reader.ReadBits(8))
        {
            case SessionMessage.Welcome:
                EntityId = (int)reader.ReadVarUInt();
                TickRate = (int)reader.ReadVarUInt();
                Map = reader.ReadString(64);
                Clock = new TickClockSync(Math.Max(1, TickRate));
                break;
            case SessionMessage.Scoreboard:
                Scoreboard = Multiplayer.Scoreboard.Read(ref reader) ?? Scoreboard;
                break;
            case SessionMessage.Snapshot when Clock is { } clock:
                int part = (int)reader.ReadBits(8);
                int parts = (int)reader.ReadBits(8);
                if (!SnapshotCodec.Read(ref reader, Received.Get, out Snapshot? piece) || piece is null || parts < 1 || part >= parts)
                {
                    DroppedSnapshots++;
                    return;
                }

                long ran = reader.ReadBits(32) | ((long)reader.ReadBits(32) << 32);
                short lead = (short)(ushort)reader.ReadBits(16);
                if (Assemble(piece, part, parts) is not { } snapshot)
                {
                    return;
                }

                Received.Add(snapshot);
                Remote.Add(snapshot);
                if (snapshot.Tick > _newestReceived)
                {
                    _newestReceived = snapshot.Tick;
                    Latest = snapshot;
                    LastRunCommandTick = reader.IsOverflowed ? long.MinValue : ran;
                    clock.AddSample(snapshot.Tick, now, _net.Connection?.Statistics.RoundTripTime ?? 0);
                    if (!reader.IsOverflowed && lead != short.MinValue)
                    {
                        CommandLead = lead;
                        clock.ReportCommandLead(lead);
                    }
                }

                break;
        }
    }
}
