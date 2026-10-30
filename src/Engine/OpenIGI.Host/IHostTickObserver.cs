using OpenIGI.Game.Player;

namespace OpenIGI.Host;

/// <summary>Something run either side of each tick of the title's player: a multiplayer session reads before and sends after.</summary>
/// <remarks>
/// The dedicated server is one: before the tick it takes in what the clients sent, so their commands are there when the
/// level runs; after it, it captures the level and sends each client its snapshot.
/// </remarks>
public interface IHostTickObserver
{
    /// <summary>Called just before the title's player runs a tick.</summary>
    /// <param name="player">The title's player.</param>
    void BeforeTitleTick(ITitlePlayer player);

    /// <summary>Called just after the title's player has run a tick.</summary>
    /// <param name="player">The title's player.</param>
    void AfterTitleTick(ITitlePlayer player);
}

/// <summary>The host's observers.</summary>
public sealed partial class GameHost
{
    /// <summary>Gets or sets what runs either side of each tick of the title's player, or <see langword="null"/>.</summary>
    public IHostTickObserver? TickObserver { get; set; }
}
