using System.Collections.Concurrent;

namespace OpenIGI.Net.Rooms;

/// <summary>
/// A player reached through the room itself rather than over WebRTC: where WebRTC is not allowed (a Discord Activity), the
/// room's master carries each datagram over the socket the player already holds to it. Open from the start, with no
/// offer or answer to swap; the room says when the player has gone.
/// </summary>
/// <param name="send">Sends a datagram to this player, through the room.</param>
internal sealed class RelayedPeer(Action<ReadOnlyMemory<byte>> send) : IRtcPeer
{
    // A player's datagrams waiting to be read, at most: past it the oldest are dropped, as a full line would.
    private const int MaxWaiting = 512;

    private readonly ConcurrentQueue<byte[]> _arrived = new();
    private bool _disposed;

    /// <inheritdoc/>
    public RtcPeerState State => _disposed ? RtcPeerState.Closed : RtcPeerState.Open;

    /// <inheritdoc/>
    public string? Failure => null;

    /// <inheritdoc/>
    public bool Relayed => true;

    /// <inheritdoc/>
    public string Route => "room relay";

    /// <inheritdoc/>
    public void SetRemoteDescription(string kind, string sdp)
    {
        // Nothing to swap: the room carries everything.
    }

    /// <inheritdoc/>
    public void AddRemoteCandidate(string candidate, string? mid)
    {
        // Nothing to swap: the room carries everything.
    }

    /// <inheritdoc/>
    public bool TryTakeSignal(out RtcSignal signal)
    {
        signal = default;
        return false;
    }

    /// <inheritdoc/>
    public void Send(ReadOnlySpan<byte> datagram)
    {
        if (!_disposed)
        {
            send(datagram.ToArray());
        }
    }

    /// <summary>A datagram the room brought from this player.</summary>
    /// <param name="datagram">The bytes.</param>
    public void Arrived(byte[] datagram)
    {
        _arrived.Enqueue(datagram);
        while (_arrived.Count > MaxWaiting && _arrived.TryDequeue(out _))
        {
            // The oldest go first.
        }
    }

    /// <inheritdoc/>
    public bool TryReceive(Span<byte> buffer, out int length)
    {
        while (_arrived.TryDequeue(out byte[]? datagram))
        {
            if (datagram.Length <= buffer.Length)
            {
                datagram.CopyTo(buffer);
                length = datagram.Length;
                return true;
            }
        }

        length = 0;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose() => _disposed = true;
}
