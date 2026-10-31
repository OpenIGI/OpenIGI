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

    /// <summary>Gets which of the server's levels this client is on, counted on at each travel.</summary>
    public byte Epoch { get; private set; }

    private string? _travel;

    // What the server told this client was said and marked, since it last took them.
    private readonly List<ChatLine> _heard = [];
    private readonly List<WorldMark> _marked = [];

    /// <summary>Says a line to every player.</summary>
    /// <param name="text">The line.</param>
    public void Say(string text)
    {
        _writer.Reset();
        SessionTalk.WriteChat(_writer, -1, text);
        _net.Send(NetChannel.Reliable, _writer.AsSpan());
    }

    /// <summary>Marks a place for every player.</summary>
    /// <param name="position">Where, in world units.</param>
    public void Mark(Int3 position)
    {
        _writer.Reset();
        SessionTalk.WriteMark(_writer, -1, position);
        _net.Send(NetChannel.Reliable, _writer.AsSpan());
    }

    /// <summary>Takes what has been said since the last call, this client's own lines included as the server echoed them.</summary>
    /// <returns>The lines, oldest first.</returns>
    public IReadOnlyList<ChatLine> TakeChat()
    {
        ChatLine[] heard = [.. _heard];
        _heard.Clear();
        return heard;
    }

    /// <summary>Takes what has been marked since the last call, this client's own marks included as the server echoed them.</summary>
    /// <returns>The marks, oldest first.</returns>
    public IReadOnlyList<WorldMark> TakeMarks()
    {
        WorldMark[] marked = [.. _marked];
        _marked.Clear();
        return marked;
    }

    /// <summary>Takes the map the server has gone on to, once, for the client to load.</summary>
    /// <returns>The map, or <see langword="null"/> when the server has not travelled since the last call.</returns>
    public string? TakeTravel()
    {
        string? travel = _travel;
        _travel = null;
        return travel;
    }

    /// <summary>Gets the estimate of the server's clock, once welcomed.</summary>
    public TickClockSync? Clock { get; private set; }

    /// <summary>Gets the snapshots that arrived, for drawing the other humans between them.</summary>
    public SnapshotInterpolator Remote { get; } = new(32);

    /// <summary>Gets the newest snapshot that arrived, or <see langword="null"/>.</summary>
    public Snapshot? Latest { get; private set; }

    /// <summary>Gets the newest scoreboard, or <see langword="null"/>.</summary>
    public Scoreboard? Scoreboard { get; private set; }

    /// <summary>Gets the server level's running state as last sent, or <see langword="null"/> before any.</summary>
    public byte[]? Level { get; private set; }

    private bool _levelTaken;

    /// <summary>Takes the server level's running state once each time a new one comes.</summary>
    /// <returns>The state, or <see langword="null"/> when none has come since the last call.</returns>
    public byte[]? TakeLevel()
    {
        if (_levelTaken || Level is null)
        {
            return null;
        }

        _levelTaken = true;
        return Level;
    }

    /// <summary>Gets why the session ended or never started, or <see langword="null"/> while it goes on.</summary>
    public string? EndReason { get; private set; }

    /// <summary>Gets or sets how many of the newest commands each packet carries (<c>cl_cmdbackup</c>).</summary>
    public int CommandRedundancy { get; set; } = 3;

    /// <summary>Gets the newest commands sent, oldest first, for prediction to run again.</summary>
    public IReadOnlyList<UserCommand> SentCommands => _sent;

    /// <summary>Gets the newest tick whose command the server has run for this client, as of the newest snapshot.</summary>
    public long LastRunCommandTick { get; private set; } = long.MinValue;

    /// <summary>Gets how many of this client's commands the server last said were waiting to be run: its added delay, in ticks.</summary>
    public int CommandLead { get; private set; }

    /// <summary>Gets what keeps this client's own player where the server has it.</summary>
    public PredictionReconciler Reconciler { get; private set; } = new();

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

        // One command for each tick this client ran, numbered on from the last: the server runs exactly these, in this order,
        // so its copy of the player goes where this one went. Only the first is placed by the clock.
        long tick = _sent.Count > 0 ? _sent[^1].Tick + 1 : clock.CommandTick(now, connection.Statistics.RoundTripTime);
        long viewTick = (long)Math.Floor(clock.InterpolationTick(now));
        Keep(new UserCommand(tick, channels.ToArray(), viewTick));
        int count = Math.Clamp(CommandRedundancy, 1, UserCommandCodec.MaxCommandsPerPacket);
        int first = _sent.Count - 1;
        while (first > 0 && _sent.Count - first < count && _sent[first - 1].Tick == _sent[first].Tick - 1)
        {
            first--;
        }

        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Commands, 8);
        _writer.WriteBits(Epoch, 8);
        _writer.WriteBits((uint)_newestReceived, 32);
        _writer.WriteBits((uint)(_newestReceived >> 32), 32);
        _codec.Write(_writer, _sent.ToArray().AsSpan(first));
        _net.Send(NetChannel.Sequenced, _writer.AsSpan());
        return tick;
    }

    /// <summary>Sends what is queued.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Flush(double now) => _net.Flush(now);

    // Everything that belonged to the level the server has left: its snapshots, its clock, the commands sent for it.
    private void ForgetLevel()
    {
        _sent.Clear();
        _parts.Clear();
        _newestReceived = -1;
        Received.Clear();
        Remote.Clear();
        Latest = null;
        Level = null;
        _levelTaken = false;
        LastRunCommandTick = long.MinValue;
        CommandLead = 0;
        Reconciler = new PredictionReconciler();
        Clock = new TickClockSync(Math.Max(1, TickRate));
    }

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
                Epoch = (byte)reader.ReadBits(8);
                Clock = new TickClockSync(Math.Max(1, TickRate));
                break;
            case SessionMessage.Chat when SessionTalk.ReadChat(ref reader) is { } line:
                _heard.Add(line);
                break;
            case SessionMessage.Mark when SessionTalk.ReadMark(ref reader) is { } mark:
                _marked.Add(mark);
                break;
            case SessionMessage.Travel:
                Epoch = (byte)reader.ReadBits(8);
                EntityId = (int)reader.ReadVarUInt();
                Map = reader.ReadString(64);
                ForgetLevel();
                _travel = Map;
                break;
            case SessionMessage.Scoreboard:
                Scoreboard = Multiplayer.Scoreboard.Read(ref reader) ?? Scoreboard;
                break;
            case SessionMessage.Level:
                uint length = reader.ReadVarUInt();
                if (length <= 65536)
                {
                    byte[] level = new byte[length];
                    for (int i = 0; i < level.Length; i++)
                    {
                        level[i] = (byte)reader.ReadBits(8);
                    }

                    if (!reader.IsOverflowed)
                    {
                        Level = level;
                        _levelTaken = false;
                    }
                }

                break;
            case SessionMessage.Snapshot when Clock is { } clock:
                int part = (int)reader.ReadBits(8);
                int parts = (int)reader.ReadBits(8);
                if (reader.ReadBits(8) != Epoch)
                {
                    return;
                }

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
                    }
                }

                break;
        }
    }
}
