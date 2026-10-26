using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// I.G.I. 2's explosion, <c>Explosion0_s</c>: what it is and the fragments it throws; it goes off through
/// <c>Explosion0_CreateExplosion</c>.
/// </summary>
public sealed class Igi2Explosion : Explosion0
{
    private readonly Igi2ExplosionWorld _world;

    /// <summary>Initializes a new instance of the <see cref="Igi2Explosion"/> class: the task half of <c>Explosion0_NewEx</c>.</summary>
    /// <param name="world">The world it goes off in.</param>
    /// <param name="request">What it is.</param>
    /// <param name="direction">Its fragments' direction, normalised.</param>
    /// <param name="fragments">The container its fragments fly in.</param>
    internal Igi2Explosion(Igi2ExplosionWorld world, Igi2ExplosionRequest request, Vector3f direction, ExplosionFragments fragments)
        : base(world.Random, request.Count, request.Period)
    {
        _world = world;
        Request = request;
        Direction = direction;
        Fragments = fragments;
    }

    /// <summary>Gets what it is.</summary>
    public Igi2ExplosionRequest Request { get; }

    /// <summary>Gets its fragments' direction, <c>tDirection</c>.</summary>
    public Vector3f Direction { get; }

    /// <summary>Gets the container its fragments fly in.</summary>
    public ExplosionFragments Fragments { get; }

    /// <inheritdoc/>
    protected override void GoOff() => _world.GoOff(this);
}
