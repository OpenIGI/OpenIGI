using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// An explosion going off, <c>Explosion0_s</c> (<c>Explosion0_RunHandler</c>, <c>igi2 0x50A100</c>): it waits out its timer,
/// goes off, and waits a random share of its period before each further time.
/// </summary>
public sealed class Igi2Explosion : IIgi2DynamicTask
{
    private readonly Igi2ExplosionWorld _world;

    /// <summary>Initializes a new instance of the <see cref="Igi2Explosion"/> class: the task half of <c>Explosion0_NewEx</c>.</summary>
    /// <param name="world">The world it goes off in.</param>
    /// <param name="request">What it is.</param>
    /// <param name="direction">Its fragments' direction, normalised.</param>
    /// <param name="fragments">The container its fragments fly in.</param>
    internal Igi2Explosion(Igi2ExplosionWorld world, Igi2ExplosionRequest request, Vector3f direction, Igi2FragmentContainer fragments)
    {
        _world = world;
        Request = request;
        Direction = direction;
        Fragments = fragments;
        Count = request.Count;
        Timer = request.Period;
    }

    /// <summary>Gets what it is.</summary>
    public Igi2ExplosionRequest Request { get; }

    /// <summary>Gets its fragments' direction, <c>tDirection</c>.</summary>
    public Vector3f Direction { get; }

    /// <summary>Gets the container its fragments fly in.</summary>
    public Igi2FragmentContainer Fragments { get; }

    /// <summary>Gets how many more times it goes off, <c>nExplosion</c>.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the ticks before it next goes off.</summary>
    public int Timer { get; private set; }

    /// <inheritdoc/>
    /// <remarks>Each time but the last is followed by a wait of the run stream's next word modulo the period.</remarks>
    public bool Run()
    {
        if (Timer != 0)
        {
            Timer--;
        }

        if (Timer != 0 || Count == 0)
        {
            return true;
        }

        _world.GoOff(this);
        Count--;
        if (Count == 0)
        {
            return false;
        }

        Timer = (int)(_world.Random.NextRaw() % (uint)Request.Period);
        return true;
    }
}
