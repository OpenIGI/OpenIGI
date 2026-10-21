using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The 2-D particles (<c>Particle2DDynCubeObj</c>) and the blood thrown through them (<c>HumanDamage_CreateFleshEnterEffect</c>).</summary>
public sealed class Igi2ParticleTests
{
    /// <summary>A particle lives its ticks, then fades by its step; once none is left the system deletes itself on its next run.</summary>
    [Fact]
    public void A_particle_lives_fades_and_its_system_deletes_itself()
    {
        Igi2Particles module = new(new Igi2PlayerFakes.FakeCollision(), new Igi2DynamicTasks());
        Igi2Particle2DSystem system = module.New("s", false, 0, default, default, 1f, 1f, 0f, 0, 0, 0f, 0f, 4, null);
        system.AddParticle(default, new Vector3f(1f, 0f, 0f), intensity: 1f, 0f, 0f, size: 1f, lifeTime: 1, fadeTime: 2);

        Assert.True(system.Run());
        Assert.Equal(1, system.Count);
        Assert.Equal(1.0, system.Particles.Single().Position.X);

        Assert.True(system.Run());
        Assert.Equal(0.5f, system.Particles.Single().Intensity);

        Assert.True(system.Run());
        Assert.Equal(0, system.Count);

        Assert.False(system.Run());
        Assert.True(system.IsDeleted);
    }

    /// <summary>A particle is checked against the level every fifteen ticks, from where it was last checked; one that strikes dies.</summary>
    [Fact]
    public void A_particle_that_strikes_a_surface_is_handed_over_and_dies()
    {
        Igi2PlayerFakes.FakeCollision collision = new();
        collision.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(-10.0));
        Igi2Particles module = new(collision, new Igi2DynamicTasks());
        List<Igi2ParticleCollision> struck = [];
        Igi2Particle2DSystem system = module.New("s", false, 0, default, default, 1f, 1f, 0f, 0, 0, 0f, 0f, 4, struck.Add);

        // The first particle takes collision phase 0: its countdown is already out, so it is checked on the first run.
        system.AddParticle(default, new Vector3f(0f, 0f, -20f), 1f, 0f, 0f, 1f, 100, 0);
        system.Run();

        Igi2ParticleCollision hit = Assert.Single(struck);
        Assert.Equal(-20.0, hit.End.Z);
        Assert.Equal(0, system.Count);
    }

    /// <summary>
    /// A cone particle's angle, turn and speed come from the module's own stream, three draws, and never from the run stream.
    /// </summary>
    [Fact]
    public void A_cone_particle_draws_three_from_the_module_stream()
    {
        Igi2Particles module = new(new Igi2PlayerFakes.FakeCollision(), new Igi2DynamicTasks());
        IgiRandom twin = new(unchecked((uint)-624411820), unchecked((uint)-896272554), 430872269u);
        Igi2Particle2DSystem system = module.New("s", false, 0, default, default, 1f, 1f, 0f, 0, 0, 0f, 0f, 4, null);

        system.AddConeParticle(default, new Vector3f(0f, 0f, 1f), 0.5f, 100f, 200f, 1f, 0f, 0f, 1f, 10, 0);

        twin.NextRaw();
        twin.NextRaw();
        twin.NextRaw();
        Assert.Equal(twin.NextRaw(), module.Random.NextRaw());
        float speed = system.Particles.Single().Speed is { } v ? MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z)) : 0f;
        Assert.InRange(speed, 99.99f, 200.01f);
    }

    /// <summary>
    /// A wound with nothing to stain behind it draws from the run stream a count of 12 to 14 forward drops and a spin for each, then
    /// a count of 16 to 18 back and a spin for each.
    /// </summary>
    [Fact]
    public void A_wound_draws_its_drops_from_the_run_stream()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        Igi2DynamicTasks dynamic = new();
        surroundings.Particles = new Igi2Particles(new Igi2PlayerFakes.FakeCollision(), dynamic);
        player.Random = new IgiRandom(7u);
        IgiRandom twin = new(7u);

        player.CreateFleshEnterEffect(new Vector3f(1f, 0f, 0f), default, player);

        int forward = (int)(twin.NextRaw() % 3) + 12;
        for (int i = 0; i < forward; i++)
        {
            twin.NextRaw();
        }

        int backward = (int)(twin.NextRaw() % 3) + 16;
        for (int i = 0; i < backward; i++)
        {
            twin.NextRaw();
        }

        Assert.Equal(twin.NextRaw(), player.Random.NextRaw());
        Assert.Equal(2, dynamic.Tasks.Count);
        Assert.Equal(Math.Min(20, forward + backward), ((Igi2Particle2DSystem)dynamic.Tasks[1]).Count);
    }
}
