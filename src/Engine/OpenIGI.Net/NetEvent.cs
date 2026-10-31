namespace OpenIGI.Net;

/// <summary>What happened on a server or client.</summary>
public enum NetEventKind : byte
{
    /// <summary>A connection was established.</summary>
    Connected,

    /// <summary>A connection ended; <see cref="NetEvent.Reason"/> says why.</summary>
    Disconnected,

    /// <summary>A message arrived.</summary>
    Message,

    /// <summary>The server turned the connection attempt down; <see cref="NetEvent.Reason"/> says why. Client only.</summary>
    Rejected,
}

/// <summary>Something a server or client reports from its update.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Peer">The client's slot on a server; on a client, the slot the server gave it.</param>
/// <param name="Message">The message, for <see cref="NetEventKind.Message"/>.</param>
/// <param name="Reason">Why a connection ended or was turned down.</param>
/// <param name="Payload">The application bytes the other end sent with an acceptance, for <see cref="NetEventKind.Connected"/>.</param>
public readonly record struct NetEvent(NetEventKind Kind, int Peer, NetMessage Message, string Reason, byte[]? Payload);

/// <summary>A client asking to join, as the server's admission sees it.</summary>
/// <param name="Address">Where it connects from.</param>
/// <param name="Payload">What it sent with the request: its name, its game, the map it has, a password.</param>
/// <param name="Slot">The slot it will take if admitted.</param>
public readonly record struct NetConnectRequest(NetAddress Address, byte[] Payload, int Slot);

/// <summary>The server's answer to a <see cref="NetConnectRequest"/>.</summary>
/// <param name="IsAccepted">Whether the client may join.</param>
/// <param name="Reason">Why not, shown to the player.</param>
/// <param name="Payload">What to send the client with an acceptance.</param>
public readonly record struct NetAdmission(bool IsAccepted, string Reason, byte[] Payload)
{
    /// <summary>Gets an acceptance.</summary>
    /// <param name="payload">What to send the client with it.</param>
    /// <returns>The admission.</returns>
    public static NetAdmission Accept(byte[]? payload = null) => new(true, string.Empty, payload ?? []);

    /// <summary>Gets a refusal.</summary>
    /// <param name="reason">Why, shown to the player.</param>
    /// <returns>The admission.</returns>
    public static NetAdmission Reject(string reason) => new(false, reason, []);
}
