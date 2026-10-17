namespace OpenIGI.Game.Player;

/// <summary>What a title's player can do beyond <see cref="ITitlePlayer"/>, read once when it is stood up.</summary>
/// <remarks>
/// Each optional seam a title's player may implement -- a look the frame can preview, hands the first-person view draws,
/// models it shows in the world, a placement it can be moved to, diagnostics -- is asked about here, once, rather than
/// cast for at every place that uses it.
/// </remarks>
public sealed class TitlePlayerFeatures
{
    /// <summary>Initializes a new instance of the <see cref="TitlePlayerFeatures"/> class.</summary>
    /// <param name="player">The title's player.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is <see langword="null"/>.</exception>
    public TitlePlayerFeatures(ITitlePlayer player)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Look = player as ITitlePlayerLook;
        FirstPerson = player as ITitleFirstPerson;
        WorldModels = player as ITitleWorldModels;
        Placement = player as ITitlePlayerPlacement;
        Diagnostics = player as ITitlePlayerDiagnostics;
    }

    /// <summary>Gets the player.</summary>
    public ITitlePlayer Player { get; }

    /// <summary>Gets its look, which the frame previews between ticks, when it has one of its own.</summary>
    public ITitlePlayerLook? Look { get; }

    /// <summary>Gets what it holds in front of its eyes, when the first-person view draws it.</summary>
    public ITitleFirstPerson? FirstPerson { get; }

    /// <summary>Gets the models it shows in the world, when it has any.</summary>
    public ITitleWorldModels? WorldModels { get; }

    /// <summary>Gets where it can be moved to, when it can be.</summary>
    public ITitlePlayerPlacement? Placement { get; }

    /// <summary>Gets what it reports for diagnosis, when it reports anything.</summary>
    public ITitlePlayerDiagnostics? Diagnostics { get; }
}
