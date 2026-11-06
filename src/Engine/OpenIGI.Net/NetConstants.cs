namespace OpenIGI.Net;

/// <summary>The fixed sizes and limits of the wire format.</summary>
public static class NetConstants
{
    /// <summary>The largest datagram sent, under the common internet MTU after IP and UDP headers.</summary>
    public const int MaxPacketSize = 1200;

    /// <summary>The bytes every packet starts with: its kind and its CRC.</summary>
    public const int PacketPrefixSize = 5;

    /// <summary>The bytes a payload packet's header takes after the prefix: salt, sequence, ack and ack bits.</summary>
    public const int PayloadHeaderSize = 12;

    /// <summary>The largest piece a reliable message is cut into.</summary>
    public const int MaxFragmentSize = 1024;

    /// <summary>The largest reliable message, after reassembly.</summary>
    public const int MaxReliableMessageSize = 256 * 1024;

    /// <summary>The bytes a sealed payload packet carries beyond its body: the authentication tag.</summary>
    public const int SealOverhead = 16;

    /// <summary>The largest unreliable message; it must fit one packet, sealed.</summary>
    public const int MaxUnreliableMessageSize = MaxPacketSize - PacketPrefixSize - PayloadHeaderSize - SealOverhead - 8;

    /// <summary>The most packets one flush writes: no more than one packet's acknowledgement field covers.</summary>
    /// <remarks>
    /// A packet acknowledges the newest one received and the 32 before it. A flush that wrote more than that and lost the
    /// odd one would have the rest go unacknowledged for good, and every reliable message in them sent again.
    /// </remarks>
    public const int MaxPacketsPerFlush = 32;

    /// <summary>The bytes a second a connection sends unless told otherwise (Source's <c>rate</c>).</summary>
    public const int DefaultRate = 256 * 1024;

    /// <summary>The largest application payload a connection request carries.</summary>
    public const int MaxConnectPayloadSize = 900;

    /// <summary>The size a connection request is padded to, so a challenge is never larger than what provoked it.</summary>
    public const int ConnectionRequestSize = 256;

    /// <summary>The packets and reliable messages a connection keeps track of at once.</summary>
    public const int WindowSize = 1024;

    /// <summary>The reliable messages that may be unacknowledged at once; the rest wait their turn.</summary>
    public const int MaxReliableInFlight = 512;

    /// <summary>The bytes a transport may hold unsent for one destination before it drops what more comes, as a full socket does.</summary>
    /// <remarks>
    /// A WebRTC channel otherwise queues without end: sent faster than the line carries, every datagram waits behind the
    /// last, and the round trip grows by seconds while the line itself is quick.
    /// </remarks>
    public const int MaxSendBacklog = 32 * 1024;

    /// <summary>The bytes waiting to go to a client past which the server skips its snapshot that tick (Source's choke).</summary>
    /// <remarks>
    /// Two packets: a snapshot or two in flight. A healthy line holds next to nothing; a lossy one, slowed to a couple of KiB a
    /// second by WebRTC's congestion control, held four packets' worth for seconds, and every snapshot reached its client
    /// that much older.
    /// </remarks>
    public const int ChokeBacklog = 2 * MaxPacketSize;

    /// <summary>The bytes waiting to go to the server past which a client holds its command packet that tick.</summary>
    /// <remarks>
    /// A command packet is some seventy bytes, thirty a second: <see cref="ChokeBacklog"/> would be two seconds of them. A
    /// client sending on regardless over a lossy line -- WebRTC's congestion control then carries a couple of KiB a second
    /// -- queued every command behind the last, and its round trip climbed by seconds without end. Held at this, the
    /// commands wait no longer than a quarter of a second; the next packet sent carries those held, as many as fit.
    /// </remarks>
    public const int CommandChokeBacklog = 512;
}

/// <summary>How a message is delivered.</summary>
public enum NetChannel : byte
{
    /// <summary>At most once, in any order: a lost message is gone.</summary>
    Unreliable = 0,

    /// <summary>At most once, never older than one already delivered: snapshots and user commands.</summary>
    Sequenced = 1,

    /// <summary>Exactly once, in the order sent, however large: game events, chat, commands.</summary>
    Reliable = 2,
}

/// <summary>The kind of a packet, its first byte.</summary>
internal enum PacketKind : byte
{
    ConnectionRequest = 1,
    Challenge = 2,
    ChallengeResponse = 3,
    Accepted = 4,
    Rejected = 5,
    Payload = 6,
    Disconnect = 7,
    InfoRequest = 8,
    InfoResponse = 9,
}

/// <summary>What a client's connection request offers, a bit each.</summary>
[Flags]
internal enum ConnectFlags : byte
{
    None = 0,

    /// <summary>The client can seal its packets and wants the server's key.</summary>
    Sealed = 1,
}
