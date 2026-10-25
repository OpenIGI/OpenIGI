using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// I.G.I. 2's player, standing in another game's level, as that game's rounds find it: its own bone collider, for the level's
/// player task, which takes the hit and hands it to the body (<see cref="Igi2PlayerInHost"/>).
/// </summary>
/// <param name="task">The level's player task.</param>
/// <param name="human">The player.</param>
public sealed class Igi2ShootablePlayer(GameTask task, Igi2HumanPlayer human) : IShootableBodies, IShootableBody
{
    private readonly WorldLineTrace _trace = new(1);

    /// <inheritdoc/>
    public float DamageScale { get; init; } = 1f;

    /// <inheritdoc/>
    public GameTask Task => task;

    /// <inheritdoc/>
    /// <remarks>A dead player is not there to be struck: the round passes where the body fell.</remarks>
    public IEnumerable<IShootableBody> Bodies()
    {
        if (!human.IsDead)
        {
            yield return this;
        }
    }

    /// <inheritdoc/>
    /// <remarks>The player hears a round go by as its own game's rounds tell it; another game's tell it nothing.</remarks>
    public void RoundPassed(Vector3d origin, Vector3d target, GameTask? struck)
    {
    }

    /// <inheritdoc/>
    public bool TryMeet(Vector3d origin, Vector3d target, out HumanHit hit)
    {
        hit = default;
        if (human.Collider is not { } collider)
        {
            return false;
        }

        _trace.Starts[0] = origin;
        _trace.Ends[0] = target;
        _trace.Count = 1;
        _trace.Intersections[0] = new WorldIntersection();
        collider.TraceLines(_trace);
        WorldIntersection met = _trace.Intersections[0];
        if (!met.IsHit)
        {
            return false;
        }

        hit = new HumanHit(met.Position, Vector3d.Distance(origin, met.Position), HumanDamage.DefaultZone, 0f, 0f, met.Sector);
        return true;
    }
}
