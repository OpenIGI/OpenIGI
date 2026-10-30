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

    /// <summary>The largest unreliable message; it must fit one packet.</summary>
    public const int MaxUnreliableMessageSize = MaxPacketSize - PacketPrefixSize - PayloadHeaderSize - 8;

    /// <summary>The largest application payload a connection request carries.</summary>
    public const int MaxConnectPayloadSize = 900;

    /// <summary>The size a connection request is padded to, so a challenge is never larger than what provoked it.</summary>
    public const int ConnectionRequestSize = 256;

    /// <summary>The packets and reliable messages a connection keeps track of at once.</summary>
    public const int WindowSize = 1024;

    /// <summary>The reliable messages that may be unacknowledged at once; the rest wait their turn.</summary>
    public const int MaxReliableInFlight = 512;
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
