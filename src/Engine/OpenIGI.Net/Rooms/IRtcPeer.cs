namespace OpenIGI.Net.Rooms;

/// <summary>A STUN or TURN server a WebRTC connection may use, as a room hands it out.</summary>
/// <param name="Urls">Its addresses: <c>stun:</c>, <c>turn:</c> or <c>turns:</c> URLs.</param>
/// <param name="Username">The relay login's name, or <see langword="null"/> for STUN.</param>
/// <param name="Credential">The relay login's password, or <see langword="null"/> for STUN.</param>
public sealed record IceServer(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null);

/// <summary>Where a <see cref="IRtcPeer"/> is in its life.</summary>
public enum RtcPeerState
{
    /// <summary>Still finding a way to the other side.</summary>
    Connecting,

    /// <summary>The data channel is open: datagrams go both ways.</summary>
    Open,

    /// <summary>Failed or closed; it will not open again.</summary>
    Closed,
}

/// <summary>What one side of a WebRTC connection has to tell the other, by way of the room.</summary>
/// <param name="Kind"><c>offer</c>, <c>answer</c> or <c>candidate</c>.</param>
/// <param name="Value">The session description, or the candidate line.</param>
/// <param name="Mid">A candidate's media id; <see langword="null"/> for a description.</param>
public readonly record struct RtcSignal(string Kind, string Value, string? Mid = null);

/// <summary>
/// One WebRTC connection to another player, carrying the game's datagrams on a data channel that neither orders nor
/// resends them: the connection layer above already does both, and does them better for a game.
/// </summary>
/// <remarks>
/// A desktop build's is libdatachannel's; a browser's is the page's <c>RTCPeerConnection</c>. Calls come from the
/// network's one thread; anything the connection reports on its own threads is queued for <see cref="TryTakeSignal"/>
/// and <see cref="TryReceive"/>.
/// </remarks>
public interface IRtcPeer : IDisposable
{
    /// <summary>Gets where the connection is.</summary>
    RtcPeerState State { get; }

    /// <summary>Gets why it closed, if it failed.</summary>
    string? Failure { get; }

    /// <summary>Applies the other side's offer or answer.</summary>
    /// <param name="kind"><c>offer</c> or <c>answer</c>.</param>
    /// <param name="sdp">The description.</param>
    void SetRemoteDescription(string kind, string sdp);

    /// <summary>Adds one of the other side's candidates.</summary>
    /// <param name="candidate">The candidate line.</param>
    /// <param name="mid">Its media id.</param>
    void AddRemoteCandidate(string candidate, string? mid);

    /// <summary>Takes the next thing this side has to tell the other, if any.</summary>
    /// <param name="signal">It.</param>
    /// <returns><see langword="true"/> when one was taken.</returns>
    bool TryTakeSignal(out RtcSignal signal);

    /// <summary>Sends a datagram, if the channel is open; otherwise drops it, as a network would.</summary>
    /// <param name="datagram">The bytes.</param>
    void Send(ReadOnlySpan<byte> datagram);

    /// <summary>Takes the next datagram that arrived, if any.</summary>
    /// <param name="buffer">Where to copy it.</param>
    /// <param name="length">How many bytes it holds.</param>
    /// <returns><see langword="true"/> when one was taken.</returns>
    bool TryReceive(Span<byte> buffer, out int length);
}

/// <summary>Makes <see cref="IRtcPeer"/>s: the one platform-specific part of playing through a room.</summary>
public interface IRtcPeerFactory
{
    /// <summary>Starts a connection.</summary>
    /// <param name="iceServers">The STUN and TURN servers it may use.</param>
    /// <param name="offerer">Whether this side makes the offer (a joiner) or answers one (the host).</param>
    /// <returns>The connection, connecting.</returns>
    IRtcPeer Create(IReadOnlyList<IceServer> iceServers, bool offerer);
}
