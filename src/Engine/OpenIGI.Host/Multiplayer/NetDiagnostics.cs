using OpenIGI.Net;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// The connection readout a session writes to the log every <see cref="IntervalSeconds"/>: where a player's round trip goes --
/// the line, a queue at either end, a frame rate that reads late -- which the scoreboard's one number can't say.
/// </summary>
/// <remarks>Logged under <see cref="LogCategory"/>, which the desktop's log file records at Information.</remarks>
public static class NetDiagnostics
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "NetDiag";

    /// <summary>How often a session writes its readout.</summary>
    public const double IntervalSeconds = 2.0;

    /// <summary>Describes a peer's route through a transport, for the readout.</summary>
    /// <param name="transport">The transport.</param>
    /// <param name="address">The peer.</param>
    /// <returns>Its route and what was dropped for it, or the address for a transport without rooms.</returns>
    public static string Route(INetTransport transport, NetAddress address) =>
        (transport as RoomTransport ?? (transport as MultiTransport)?.Part<RoomTransport>()) is { } room && address.Kind == NetAddressKind.Peer ? $"route {room.Route(address)} dropped {room.Dropped(address)}" : $"udp {address}";
}
