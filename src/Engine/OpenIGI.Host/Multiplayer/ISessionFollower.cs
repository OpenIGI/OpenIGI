namespace OpenIGI.Host.Multiplayer;

/// <summary>A session whose level is another machine's to choose: a client, which goes where its server goes.</summary>
public interface ISessionFollower
{
    /// <summary>Takes the map the server has gone on to, once, for the host to load.</summary>
    /// <returns>The map, or <see langword="null"/> when there is nowhere new to go.</returns>
    string? TakeTravel();

    /// <summary>Gets why the session ended -- the server shut down, the line to it dropped -- or <see langword="null"/> while it lives.</summary>
    string? Ended { get; }

    /// <summary>Gets the room's code, to join it again by, or <see langword="null"/> for a session reached otherwise.</summary>
    string? RoomCode { get; }
}
