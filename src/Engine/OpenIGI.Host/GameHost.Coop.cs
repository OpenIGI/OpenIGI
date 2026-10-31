using OpenIGI.Core.Diagnostics;
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

    // Into a mission from over a played one -- a restart, or a co-op client following its server -- behind the same going-in
    // sequence: the frozen level under the menu is the picture held, the menu leaves, and the place comes back as asked.
    private void GoIntoMission(FrameLoopState state, int missionId)
    {
        if (_loadSequence.IsActive)
        {
            return;
        }

        void Go()
        {
            state.Shell?.ClosePauseMenu();
            _menuFlow.MenuMode = false;
            _menuFlow.PauseMenu = false;
            ApplyGameplayMotionBlur();
            StartMission(missionId);
            _window.CursorVisible = false;
        }

        if (CinematicLoadAvailable)
        {
            BeginCinematicLoad(missionId, Go);
        }
        else
        {
            Go();
        }
    }

    // A co-op client goes where its server goes: on to the next mission, or into the same one again.
    private void FollowSessionTravel(FrameLoopState state)
    {
        if (TickObserver is ISessionFollower follower
            && follower.TakeTravel() is { } map
            && int.TryParse(map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int missionId))
        {
            _logger.Information("Coop", $"Following the room's host into mission {missionId}.");
            GoIntoMission(state, missionId);
        }
    }

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
