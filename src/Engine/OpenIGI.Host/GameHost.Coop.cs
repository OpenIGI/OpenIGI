using OpenIGI.Host.Multiplayer;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host;

/// <summary>Co-op through rooms, from the menu.</summary>
public sealed partial class GameHost
{
    private CoopRooms? _coop;

    /// <summary>Gets or sets what makes this build's WebRTC connections, or <see langword="null"/> where it has none.</summary>
    /// <remarks>
    /// Set by the composition root before <see cref="Start"/>: the desktop's libdatachannel where it is built, the page's
    /// own in the browser. Without it the menu's co-op section says rooms aren't available here.
    /// </remarks>
    public IRtcPeerFactory? RoomPeers { get; set; }

    // Made once, before the shell that offers it.
    private CoopRooms? CreateCoop() => RoomPeers is { } peers
        ? new CoopRooms(
            peers,
            () => _titleRuntime.Title.Id,
            () => _configuration.Active?.Name is { Length: > 0 } name ? name : "Jones",
            () => _configuration.Active?.Difficulty ?? 1,
            () => Game.Multiplayer.MultiplayerProtocol.ContentFingerprint(_titleRuntime.Title.Id, _titleRuntime.Mods.Select(m => $"{m.Id}@{m.Version}")),
            observer => TickObserver = observer,
            PreviewLook,
            TurnLook,
            typeof(GameHost).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev",
            _logger)
        : null;
}
