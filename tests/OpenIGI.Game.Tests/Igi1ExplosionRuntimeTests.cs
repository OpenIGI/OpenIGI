using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Particles;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Project I.G.I.'s explosions as its simulation runs them (<c>0x480FE0</c>, <c>0x4810A0</c>, <c>0x47F200</c>).</summary>
public sealed class Igi1ExplosionRuntimeTests
{
    private static readonly Vector3d At = new(8192.0, 8192.0, 8192.0);

    /// <summary>A grenade's blast goes off as it is set: one FireBall of 75 ticks, three fragments, its sound and its blast.</summary>
    [Fact]
    public void AGrenadesBlastGoesOffAsItIsSet()
    {
        Igi1ExplosionRuntime runtime = new(new IgiRandom(), particles: null);
        List<string> sounds = [];
        int blasts = 0;
        runtime.Sound = (sound, _, _) => sounds.Add(sound);

        runtime.Spawn(new Igi1ExplosionRequest(At, 1, 1, 1f, 20480f, 0f, 3, 1, "explo_02_m", () => blasts++));

        ExplosionFireBall fireBall = Assert.Single(runtime.FireBalls);
        Assert.Equal(20480f, fireBall.Size);
        Assert.Equal(75, fireBall.TotalLifetime);
        Assert.Equal(3, Assert.Single(runtime.FragmentGroups).Flying.Count);
        Assert.Equal(["explo_02_m"], sounds);
        Assert.Equal(1, blasts);
    }

    /// <summary>
    /// A blast of several FireBalls lights the first a quarter smaller, and the rest 75/n ticks apart, up to fifteen later each.
    /// </summary>
    [Fact]
    public void SeveralFireBallsAreStaggeredThroughTheBlast()
    {
        Igi1ExplosionRuntime runtime = new(new IgiRandom(), particles: null);
        runtime.Spawn(new Igi1ExplosionRequest(At, 1, 0, 5f, 10240f, 7168f, 10, 5, string.Empty));

        Assert.Equal(5, runtime.FireBalls.Count);
        Assert.Equal(10240f * 0.75f, runtime.FireBalls[0].Size);
        for (int i = 1; i < 5; i++)
        {
            ExplosionFireBall fireBall = runtime.FireBalls[i];
            Assert.InRange(fireBall.Delay, 15 * i, (15 * i) + 15);
            Assert.InRange(fireBall.Size, 10240f * 0.5f, 10240f * 0.75f);
            Assert.InRange(fireBall.TotalLifetime, 37, 75);
        }
    }

    /// <summary>The blast draws its axis and spin, then seven numbers for each further FireBall and each fragment.</summary>
    [Fact]
    public void TheBlastDrawsInTheOriginalsOrder()
    {
        IgiRandom random = new();
        int draws = 0;
        random.Drew = _ => draws++;
        Igi1ExplosionRuntime runtime = new(random, particles: null);

        runtime.Spawn(new Igi1ExplosionRequest(At, 1, 0, 1f, 4096f, 0f, 5, 2, string.Empty));

        Assert.Equal(3 + 3 + 7 + (5 * 7), draws);
    }

    /// <summary>A blast that does no damage sends no blast, and one of no size does nothing at all.</summary>
    [Fact]
    public void ABlastOfNoDamageHurtsNobody()
    {
        Igi1ExplosionRuntime runtime = new(new IgiRandom(), particles: null);
        int blasts = 0;
        runtime.Spawn(new Igi1ExplosionRequest(At, 1, 0, 0f, 4096f, 0f, 2, 1, string.Empty, () => blasts++));
        Assert.Equal(0, blasts);
        Assert.Single(runtime.FireBalls);

        runtime.Clear();
        runtime.Spawn(new Igi1ExplosionRequest(At, 1, 0, 1f, 0f, 0f, 2, 1, string.Empty, () => blasts++));
        Assert.Empty(runtime.FireBalls);
        Assert.Equal(0, blasts);
    }

    /// <summary>
    /// The smoke lasts 150 ticks, more with several FireBalls, and its two puffs are particle systems: white at once, black a
    /// tenth of the way in.
    /// </summary>
    [Fact]
    public void TheSmokesPuffsAreParticleSystems()
    {
        Particle2DModule particles = new(new IgiRandom());
        Igi1ExplosionRuntime runtime = new(new IgiRandom(), particles);
        runtime.Spawn(new Igi1ExplosionRequest(At, 1, 0, 1f, 4096f, 0f, 0, 1, string.Empty));

        runtime.Tick();
        Assert.Single(particles.Systems);

        // The black puff starts at (int)(0.9 * 150) left -- 134, the float 0.9 chopped -- the seventeenth run.
        for (int tick = 2; tick <= 16; tick++)
        {
            runtime.Tick();
        }

        Assert.Single(particles.Systems);
        runtime.Tick();
        Assert.Equal(2, particles.Systems.Count);
    }

    /// <summary>Fragments fall and are gone after the container's 45 ticks; the FireBall after its 75.</summary>
    [Fact]
    public void WhatABlastLeavesRunsOutOnItsOwn()
    {
        Igi1ExplosionRuntime runtime = new(new IgiRandom(), particles: null);
        runtime.Spawn(new Igi1ExplosionRequest(At, 1, 0, 1f, 4096f, 0f, 3, 1, string.Empty));
        ExplosionFragment fragment = runtime.FragmentGroups[0].Flying[0];
        double startZ = fragment.Position.Z;

        for (int tick = 0; tick < 47; tick++)
        {
            runtime.Tick();
        }

        Assert.True(fragment.Position.Z < startZ + 10000.0);
        Assert.Empty(runtime.FragmentGroups);
        Assert.Single(runtime.FireBalls);

        for (int tick = 0; tick < 40; tick++)
        {
            runtime.Tick();
        }

        Assert.Empty(runtime.FireBalls);
    }
}
