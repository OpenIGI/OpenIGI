namespace OpenIGI.Host.Multiplayer;

/// <summary>A session this machine's own player has a look in: how the others see it, and so how it sees itself.</summary>
public interface ISessionLook
{
    /// <summary>Gets this player's look, a <see cref="Game.Multiplayer.PlayerSkins"/> id.</summary>
    string OwnSkin { get; }
}
