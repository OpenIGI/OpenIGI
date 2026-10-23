using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>What a host gives an <see cref="Igi2GuestLevel"/> to bring it up with.</summary>
/// <param name="World">The level's collision, built for I.G.I. 2 from the host's level.</param>
/// <param name="Random">The level's run stream.</param>
/// <param name="Difficulty">The difficulty the soldiers fight at.</param>
/// <param name="Player">The host's player, whom the soldiers look out for.</param>
public sealed record Igi2GuestLevelStart(Igi2CollisionWorld World, IgiRandom Random, AiDifficulty Difficulty, HumanPlayerTask Player)
{
    /// <summary>Gets where the soldiers' sounds are heard, or <see langword="null"/> where nothing is.</summary>
    public ITitleAudio? Audio { get; init; }

    /// <summary>
    /// Gets the host's other humans, which the soldiers see as they see their own: friends or foes by their team, alive or
    /// lying dead. Its own soldiers, those it has not given the guest to stand in for, are these.
    /// </summary>
    public IReadOnlyList<IAiDetectable> Others { get; init; } = [];

    /// <summary>Gets the host's doors, which the soldiers open on their way as they open their own.</summary>
    public IReadOnlyList<IAiDoor> Doors { get; init; } = [];

    /// <summary>Gets the host's own things the guest's world holds after its bodies, its player among them.</summary>
    public IReadOnlyList<IWorldLineCollider> Colliders { get; init; } = [];

    /// <summary>Gets the host level's graphs, by their task ids, which the minds walk.</summary>
    public IReadOnlyDictionary<int, GraphFile> Graphs { get; init; } = new Dictionary<int, GraphFile>();

    /// <summary>Gets what a graph rides, by its id, or <see langword="null"/> for graphs that stand still.</summary>
    public Func<int, IAttachParent?>? CarrierOf { get; init; }

    /// <summary>
    /// Gets how the soldiers see the host's player (<see cref="Igi2HostPlayerVisibility"/>), or <see langword="null"/> for a cone's
    /// full length, as a host that measures nothing of the kind would see it.
    /// </summary>
    public Igi2VisibilityTuning? Visibility { get; init; } = new();

    /// <summary>Gets the host level's baked light, its layers in the order they are asked, which the player is seen by.</summary>
    public IReadOnlyList<IBakedLight> Light { get; init; } = [];

    /// <summary>Gets where diagnostics go.</summary>
    public IOpenIgiLogger Logger { get; init; } = NullLogger.Instance;
}
