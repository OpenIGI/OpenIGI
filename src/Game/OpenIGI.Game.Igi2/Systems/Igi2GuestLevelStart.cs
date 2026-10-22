using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>What a host gives an <see cref="Igi2GuestLevel"/> to bring it up with.</summary>
/// <param name="World">The level's collision, built for I.G.I. 2 from the host's level.</param>
/// <param name="Clips">The clips the soldiers move by.</param>
/// <param name="Models">The soldiers' models, by name.</param>
/// <param name="Sounds">Where the soldiers' sounds are played.</param>
/// <param name="Random">The level's run stream.</param>
/// <param name="Difficulty">The difficulty the soldiers fight at.</param>
/// <param name="Player">The host's player, whom the soldiers look out for.</param>
/// <param name="Ticks">The ticks the level has run.</param>
public sealed record Igi2GuestLevelStart(
    Igi2CollisionWorld World,
    Igi2AnimationLibrary? Clips,
    Func<string, MefModel?> Models,
    Igi2Sounds Sounds,
    IgiRandom Random,
    AiDifficulty Difficulty,
    HumanPlayerTask Player,
    Func<int> Ticks)
{
    /// <summary>Gets the host's own things the guest's world holds after its bodies, its player among them.</summary>
    public IReadOnlyList<IWorldLineCollider> Colliders { get; init; } = [];

    /// <summary>Gets the host level's graphs, by their task ids, which the minds walk.</summary>
    public IReadOnlyDictionary<int, GraphFile> Graphs { get; init; } = new Dictionary<int, GraphFile>();

    /// <summary>Gets what a graph rides, by its id, or <see langword="null"/> for graphs that stand still.</summary>
    public Func<int, IAttachParent?>? CarrierOf { get; init; }

    /// <summary>Gets where diagnostics go.</summary>
    public IOpenIgiLogger Logger { get; init; } = NullLogger.Instance;
}
