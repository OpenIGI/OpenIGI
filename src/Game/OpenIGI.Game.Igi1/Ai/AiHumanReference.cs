using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A human offered to one soldier's scan by reference, described only when the soldier reads it.</summary>
/// <remarks>See <see cref="Igi1AiTrackedHuman"/>. The player is described when offered -- the simulation has its description to
/// hand -- and a soldier through <see cref="SoldierSightQueries.Describe"/>, from the soldier looking's side.</remarks>
internal readonly struct AiHumanReference
{
    private readonly AiDetectionCandidate _known;
    private readonly SoldierSightQueries? _queries;
    private readonly AiHumanSource? _source;
    private readonly bool _otherSide;

    /// <summary>Initialises a new instance of the <see cref="AiHumanReference"/> struct for a human already described.</summary>
    /// <param name="known">The description.</param>
    public AiHumanReference(in AiDetectionCandidate known)
    {
        _known = known;
        Id = known.Id;
    }

    /// <summary>Initialises a new instance of the <see cref="AiHumanReference"/> struct for a soldier, described when read.</summary>
    /// <param name="queries">What describes it.</param>
    /// <param name="source">The soldier.</param>
    /// <param name="otherSide">Whether it is on the other side from the soldier looking.</param>
    public AiHumanReference(SoldierSightQueries queries, AiHumanSource source, bool otherSide)
    {
        _queries = queries;
        _source = source;
        _otherSide = otherSide;
        Id = source.Id;
    }

    /// <summary>Gets the human's id.</summary>
    public int Id { get; }

    /// <summary>Gets whether the human is on the other side from the soldier looking.</summary>
    public bool OtherSide => _source is null ? _known.OtherSide : _otherSide;

    /// <summary>Whether the human is alive now, as its description would say, without describing the rest of it.</summary>
    /// <returns><see langword="true"/> while it lives.</returns>
    public bool IsAlive() => _source is { } source ? SoldierSightQueries.IsAlive(source) : _known.Alive;

    /// <summary>Describes the human as it is now.</summary>
    /// <returns>The description.</returns>
    public AiDetectionCandidate Describe() => _source is { } source ? _queries!.Describe(source, _otherSide) : _known;
}
