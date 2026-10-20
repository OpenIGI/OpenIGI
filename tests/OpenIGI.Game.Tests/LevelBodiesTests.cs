using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A level's shot offered to every game's bodies, as the engine's trace meets its humans.</summary>
public sealed class LevelBodiesTests
{
    [Fact]
    public void The_nearest_body_that_agrees_takes_the_round_and_every_source_hears_it_pass()
    {
        Target near = new(refuses: true), middle = new(refuses: false), far = new(refuses: false);
        Source first = new([new Body(far, 30.0)]);
        Source second = new([new Body(near, 10.0), new Body(middle, 20.0)]);
        LevelBodies bodies = new();
        bodies.Add(first);
        bodies.Add(second);

        bool struck = bodies.Shoot(
            Vector3d.Zero,
            new Vector3d(100, 0, 0),
            shooter: null,
            hit => new WeaponHit(hit.Position, default, 1f, default, null, null),
            out HumanHit hit);

        Assert.True(struck);
        Assert.Equal(20.0, hit.Distance);
        Assert.Equal(1, middle.Hits);
        Assert.Equal(0, near.Hits);
        Assert.Equal(0, far.Hits);
        Assert.Same(middle, first.PassedStruck);
        Assert.Same(middle, second.PassedStruck);
    }

    [Fact]
    public void A_round_that_meets_nobody_is_heard_passing_all_the_same()
    {
        Source only = new([]);
        LevelBodies bodies = new();
        bodies.Add(only);

        Assert.False(bodies.Shoot(Vector3d.Zero, new Vector3d(1, 0, 0), null, static _ => default, out _));
        Assert.True(only.Passed);
        Assert.Null(only.PassedStruck);
    }

    [Fact]
    public void A_round_strikes_another_game_s_body_at_its_source_s_damage_scale()
    {
        Target near = new(refuses: false), far = new(refuses: false);
        LevelBodies bodies = new();
        bodies.Add(new Source([new Body(far, 30.0)]));
        bodies.Add(new Source([new Body(near, 10.0)]) { DamageScale = 0.5f });

        Assert.True(bodies.Shoot(
            Vector3d.Zero, new Vector3d(100, 0, 0), null, hit => new WeaponHit(hit.Position, default, 0.3f, default, null, null), out _));
        Assert.Equal(0.15f, near.LastDamage, 6);
        Assert.Equal(0, far.Hits);
    }

    private sealed class Target(bool refuses) : GameTask, IWeaponCollisionTarget, IWeaponHitTarget
    {
        public int Hits { get; private set; }

        public float LastDamage { get; private set; }

        public bool EvaluateWeaponCollision(in WeaponCollisionQuery query) => !refuses;

        public void WeaponHit(in WeaponHit hit)
        {
            Hits++;
            LastDamage = hit.Damage;
        }
    }

    private sealed class Body(GameTask task, double distance) : IShootableBody
    {
        public GameTask Task => task;

        public bool TryMeet(Vector3d origin, Vector3d target, out HumanHit hit)
        {
            hit = new HumanHit(new Vector3d(distance, 0, 0), distance, 0, 0f, 0f);
            return true;
        }
    }

    private sealed class Source(IReadOnlyList<IShootableBody> bodies) : IShootableBodies
    {
        public bool Passed { get; private set; }

        public GameTask? PassedStruck { get; private set; }

        public float DamageScale { get; init; } = 1f;

        public IEnumerable<IShootableBody> Bodies() => bodies;

        public void RoundPassed(Vector3d origin, Vector3d target, GameTask? struck)
        {
            Passed = true;
            PassedStruck = struck;
        }
    }
}
