namespace OpenIGI.Host.Multiplayer;

/// <summary>A session whose level is another machine's to choose: a client, which goes where its server goes.</summary>
public interface ISessionFollower
{
    /// <summary>Takes the map the server has gone on to, once, for the host to load.</summary>
    /// <returns>The map, or <see langword="null"/> when there is nowhere new to go.</returns>
    string? TakeTravel();
}
