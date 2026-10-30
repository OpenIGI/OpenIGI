namespace OpenIGI.Host;

/// <summary>Something run either side of each game tick: a multiplayer session reads before and sends after.</summary>
/// <remarks>
/// The dedicated server is one: before the tick it takes in what the clients sent, so their commands are there when the
/// level runs; after it, it captures the level and sends each client its snapshot.
/// </remarks>
public interface IHostTickObserver
{
    /// <summary>Called just before the game runs a tick.</summary>
    /// <param name="title">What is ticking: a title's own player (I.G.I. 2) or the hosted mission (I.G.I.).</param>
    void BeforeTitleTick(object title);

    /// <summary>Called just after the game has run a tick.</summary>
    /// <param name="title">What ticked.</param>
    void AfterTitleTick(object title);
}

/// <summary>The host's observers.</summary>
public sealed partial class GameHost
{
    /// <summary>Gets or sets what runs either side of each tick of the title's player, or <see langword="null"/>.</summary>
    public IHostTickObserver? TickObserver { get; set; }
}
