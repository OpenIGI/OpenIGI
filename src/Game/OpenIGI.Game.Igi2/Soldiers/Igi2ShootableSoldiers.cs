using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>A level's I.G.I. 2 soldiers as another game's shots find them: the engine's humans among the world's colliders.</summary>
/// <remarks>
/// <para>
/// A round of another game is cast at each living soldier's own bone collider, as this game's trace meets it, and the struck
/// soldier's task takes the hit, its bone carried for the zone (<see cref="Igi2WeaponHit.FromShared"/>).
/// </para>
/// <para>
/// A soldier with a mind hears a round that passes within two metres of its head (<c>Human_EvaluateGunShotHandler</c>), one
/// <c>AIEVENT_GUNSHOTMISS</c> from where it was fired, as it hears this game's bullets; a soldier of team one hears none, as
/// this game's handler compares with the weapon's team.
/// </para>
/// </remarks>
public sealed class Igi2ShootableSoldiers : IShootableBodies
{
    private readonly List<Body> _bodies = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2ShootableSoldiers"/> class.</summary>
    /// <param name="soldiers">The soldiers' tasks, whose bodies the level's gameplay made.</param>
    /// <exception cref="ArgumentNullException"><paramref name="soldiers"/> is <see langword="null"/>.</exception>
    public Igi2ShootableSoldiers(IEnumerable<Igi2HumanSoldierTask> soldiers)
    {
        ArgumentNullException.ThrowIfNull(soldiers);
        foreach (Igi2HumanSoldierTask task in soldiers)
        {
            if (task.Body is { Collider: not null } soldier)
            {
                _bodies.Add(new Body(task, soldier));
            }
        }
    }

    /// <inheritdoc/>
    public float DamageScale { get; init; } = 1f;

    /// <inheritdoc/>
    public IEnumerable<IShootableBody> Bodies()
    {
        foreach (Body body in _bodies)
        {
            // A soldier out of the world -- its container not running, or not spawned yet -- is not there to be struck.
            if (!body.Soldier.IsDead && body.Soldier.IsActive)
            {
                yield return body;
            }
        }
    }

    /// <inheritdoc/>
    public void RoundPassed(Vector3d origin, Vector3d target, GameTask? struck)
    {
        const int CreatorTeam = 1;
        foreach (Body body in _bodies)
        {
            Igi2HumanSoldier soldier = body.Soldier;
            if (soldier.Team == CreatorTeam || soldier.Mind is not Igi2HumanAi ai)
            {
                continue;
            }

            Vector3d head = soldier.CheapHeadBonePosition();
            if (Igi2GunShot.Hears(origin, target, head, out _))
            {
                float dx = ((Fpu)head.X - origin.X).F;
                float dy = ((Fpu)head.Y - origin.Y).F;
                float dz = ((Fpu)head.Z - origin.Z).F;
                float radius = (Fpu.Sqrt(((Fpu)dx * dx) + ((Fpu)dy * dy) + ((Fpu)dz * dz)) + 4096.0).F;
                ai.Receive(new Igi2AiEvent(Igi2AiEvent.GunShotMiss, 0, 1090, radius, origin, new Vector3f(dx, dy, dz), null));
            }
        }
    }

    // One soldier as a shot meets it: its task, and its own bone collider.
    private sealed class Body(Igi2HumanSoldierTask task, Igi2HumanSoldier soldier) : IShootableBody
    {
        private readonly Igi2LineTrace _trace = new(1);

        public GameTask Task => task;

        public Igi2HumanSoldier Soldier => soldier;

        public bool TryMeet(Vector3d origin, Vector3d target, out HumanHit hit)
        {
            hit = default;
            if (soldier.Collider is not { } collider)
            {
                return false;
            }

            _trace.Starts[0] = origin;
            _trace.Ends[0] = target;
            _trace.Count = 1;
            _trace.Intersections[0] = new Igi2Intersection();
            collider.TraceLines(_trace);
            Igi2Intersection met = _trace.Intersections[0];
            if (!met.IsHit)
            {
                return false;
            }

            hit = new HumanHit(met.Position, Vector3d.Distance(origin, met.Position), HumanDamage.DefaultZone, 0f, 0f, met.Sector);
            return true;
        }
    }
}
