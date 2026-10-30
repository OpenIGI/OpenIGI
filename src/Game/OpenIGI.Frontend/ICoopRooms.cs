namespace OpenIGI.Frontend;

/// <summary>Where the player's co-op room is.</summary>
public enum CoopPhase
{
    /// <summary>In no room.</summary>
    None,

    /// <summary>Opening a room, finding one, or reaching its host.</summary>
    Connecting,

    /// <summary>In a room: hosting it, or joined to its host.</summary>
    Playing,

    /// <summary>The last room failed or closed; <see cref="CoopStatus.Error"/> says why.</summary>
    Failed,
}

/// <summary>The player's co-op room as the menu shows it.</summary>
/// <param name="Phase">Where it is.</param>
/// <param name="Code">Its code, once it has one.</param>
/// <param name="Hosting">Whether this player hosts it.</param>
/// <param name="Error">Why it failed, when it did.</param>
public readonly record struct CoopStatus(CoopPhase Phase, string? Code = null, bool Hosting = false, string? Error = null);

/// <summary>A public room as the menu lists it.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="MissionId">The mission its host plays.</param>
/// <param name="Players">How many play.</param>
/// <param name="MaxPlayers">How many may.</param>
/// <param name="Passworded">Whether joining takes a password.</param>
public sealed record CoopRoomInfo(string Code, string Name, int MissionId, int Players, int MaxPlayers, bool Passworded);

/// <summary>
/// Co-op through rooms: hosting one for friends anywhere to join by its code, or joining one, on any build. The host
/// supplies it where the build can make WebRTC connections; elsewhere <see cref="IShellHost.Coop"/> is <see langword="null"/>
/// and the menu says so.
/// </summary>
public interface ICoopRooms
{
    /// <summary>Gets the player's room now.</summary>
    CoopStatus Status { get; }

    /// <summary>Gets the public rooms as last fetched, or <see langword="null"/> while fetching.</summary>
    IReadOnlyList<CoopRoomInfo>? Listing { get; }

    /// <summary>Gets why the last listing failed, when it did.</summary>
    string? ListingError { get; }

    /// <summary>Opens a room on a mission and starts it.</summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="difficulty">The difficulty.</param>
    /// <param name="listed">Whether the room is listed publicly, rather than reached by its code alone.</param>
    void Host(int missionId, int difficulty, bool listed);

    /// <summary>Joins a room by its code: loads the mission its host plays, then joins it.</summary>
    /// <param name="code">The code.</param>
    void Join(string code);

    /// <summary>Leaves the room, if in one.</summary>
    void Leave();

    /// <summary>Fetches the public rooms afresh into <see cref="Listing"/>.</summary>
    void RefreshListing();
}
