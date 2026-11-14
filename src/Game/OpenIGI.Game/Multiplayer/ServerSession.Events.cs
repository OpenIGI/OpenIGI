using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <content>
/// What each client is owed beyond the world's state: the events it has not yet been told of, a snapshot too large for a
/// packet, and who may hear whom while a round is fought.
/// </content>
public sealed partial class ServerSession
{
    // The most events a snapshot carries when it would not fit with all of them, tried in turn: the rest ride the next.
    private static readonly int[] EventBudgets = [int.MaxValue, 8, 0];

    /// <summary>Gets or sets whether a game that gives no key is turned away where this server can check one.</summary>
    /// <remarks>
    /// On by default: a player the host removed, or one banned, is known by its key and its address, and a game let in with
    /// no key is someone new each time it joins.
    /// </remarks>
    public bool RequireIdentity { get; set; } = true;

    /// <summary>Gets what a client's address is barred and banned by, beside its key.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The id, or <see langword="null"/> for a line with no address of its own (a room's peer, a loopback).</returns>
    public string? AddressIdOf(int slot) =>
        _net.GetConnection(slot) is { RemoteAddress: { Kind: NetAddressKind.Ip } address }
            ? "ip-" + address.Host.ToString("x32", System.Globalization.CultureInfo.InvariantCulture)
            : null;

    // A player put out of the room: its key and the machine it played from, neither let back in while the room stands.
    private void Bar(int slot, ClientState client)
    {
        _barred[client.PlayerId] = client.Name;
        if (AddressIdOf(slot) is { } address)
        {
            _barred[address] = client.Name;
        }
    }

    // A snapshot to one client, with the events it has not yet acknowledged. In as few packets as carry it; with fewer
    // events when they are what keeps it from fitting; and, when the world's own state is more than a packet holds -- a
    // client with no baseline yet is sent every door, wreck and task whole -- as one reliable message, which is cut and
    // put together again by the line. Without that last such a client was sent nothing, tick after tick, for good.
    private void SendSnapshot(int slot, ClientState client, Snapshot snapshot, Snapshot? baseline, long tick)
    {
        foreach (int most in EventBudgets)
        {
            Snapshot carried = client.WithEvents(snapshot, most, out int impacts, out int senses);
            if (TrySendParts(slot, carried, baseline, tick))
            {
                client.NoteSent(tick, impacts, senses);
                return;
            }
        }

        // Once a second at most: it is acknowledged a round trip later, and every snapshot after it is a small delta.
        if (client.WholeSentAt != long.MinValue && tick - client.WholeSentAt < TickRate)
        {
            return;
        }

        UserCommandBuffer? commands = _world.CommandsFor(slot);
        long ran = commands?.LastTakenTick ?? -1;
        int lead = commands is not { LastTakenTick: not long.MinValue } ? short.MinValue : Math.Min(commands.Buffered, short.MaxValue);
        Snapshot whole = client.WithEvents(snapshot, int.MaxValue, out int wholeImpacts, out int wholeSenses);
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Snapshot, 8);
        _writer.WriteBits(0, 8);
        _writer.WriteBits(1, 8);
        _writer.WriteBits(_epoch, 8);
        SnapshotCodec.Write(_writer, whole, baseline);
        _writer.WriteBits((uint)ran, 32);
        _writer.WriteBits((uint)(ran >> 32), 32);
        _writer.WriteBits((ushort)(short)lead, 16);
        if (_writer.ByteCount > NetConstants.MaxReliableMessageSize)
        {
            return;
        }

        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
        client.WholeSentAt = tick;
        client.NoteSent(tick, wholeImpacts, wholeSenses);
        client.SnapshotsSent++;
        client.SnapshotParts++;
        client.SnapshotBytes += _writer.ByteCount;
    }

    // Whether what one player says, marks or speaks is kept from another: while a round is fought, the fallen and those only
    // watching are heard by each other alone, so no one out of the round tells those still in it where the others are.
    private bool Withheld(int? speaker, int listener) =>
        speaker is { } from
        && from != listener
        && Mode is RoundMode
        && Mode.Flow.Phase == MatchPhase.Live
        && !Living(from)
        && Living(listener);

    private bool Living(int slot) => Match.Players.TryGetValue(slot, out MatchPlayer? player) && player.IsAlive;

    private sealed partial class ClientState
    {
        // The most events kept for a client that acknowledges nothing: the oldest go, and it is never told of them.
        private const int MostWaiting = 512;

        // The events this client has not acknowledged, oldest first, and the number of the first among all it was ever sent.
        private readonly List<ReplicatedImpact> _impacts = [];
        private readonly List<ReplicatedSense> _senses = [];
        private uint _impactBase;
        private uint _senseBase;

        // For each snapshot sent and not yet acknowledged, the numbers its events ran up to.
        private readonly SortedDictionary<long, (uint Impacts, uint Senses)> _sent = [];

        // The tick a snapshot last went whole by the reliable channel, or the least value before any.
        public long WholeSentAt { get; set; } = long.MinValue;

        // A tick's events, owed to this client until a snapshot that carried them is acknowledged: one riding a single
        // snapshot was lost with its packet, with a tick the line was too full for, and to every client that takes fewer
        // snapshots than the server runs ticks.
        public void Queue(ReplicatedImpact[]? impacts, ReplicatedSense[]? senses)
        {
            Add(_impacts, impacts, ref _impactBase);
            Add(_senses, senses, ref _senseBase);
        }

        // The snapshot with the oldest events waiting, as many as a snapshot carries and no more than asked.
        public Snapshot WithEvents(Snapshot snapshot, int most, out int impacts, out int senses)
        {
            impacts = Math.Min(Math.Min(_impacts.Count, most), ReplicatedImpact.MaxPerSnapshot);
            senses = Math.Min(Math.Min(_senses.Count, most), ReplicatedSense.MaxPerSnapshot);
            return snapshot with
            {
                Impacts = impacts == 0 ? null : [.. _impacts.Take(impacts)],
                Senses = senses == 0 ? null : [.. _senses.Take(senses)],
                ImpactSequence = _impactBase,
                SenseSequence = _senseBase,
            };
        }

        // A snapshot went with that many of the waiting events.
        public void NoteSent(long tick, int impacts, int senses)
        {
            _sent[tick] = (unchecked(_impactBase + (uint)impacts), unchecked(_senseBase + (uint)senses));
            while (_sent.Count > 256)
            {
                _sent.Remove(_sent.Keys.First());
            }
        }

        // The client holds the snapshot of a tick: the events that one carried are owed no longer.
        public void Acknowledge(long tick)
        {
            if (!_sent.TryGetValue(tick, out (uint Impacts, uint Senses) end))
            {
                return;
            }

            Drop(_impacts, ref _impactBase, end.Impacts);
            Drop(_senses, ref _senseBase, end.Senses);
            while (_sent.Count > 0 && _sent.Keys.First() <= tick)
            {
                _sent.Remove(_sent.Keys.First());
            }
        }

        private static void Add<T>(List<T> waiting, T[]? events, ref uint first)
        {
            if (events is null)
            {
                return;
            }

            waiting.AddRange(events);
            if (waiting.Count > MostWaiting)
            {
                int over = waiting.Count - MostWaiting;
                waiting.RemoveRange(0, over);
                first = unchecked(first + (uint)over);
            }
        }

        private static void Drop<T>(List<T> waiting, ref uint first, uint end)
        {
            int taken = Math.Clamp(unchecked((int)(end - first)), 0, waiting.Count);
            waiting.RemoveRange(0, taken);
            first = unchecked(first + (uint)taken);
        }
    }
}
