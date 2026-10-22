using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Smoke fuse, emission and cough behavior from <c>grenade.c Grenade_RunHandler</c> and <c>human.c Human_MaintainCough</c>.</summary>
public sealed class Igi2SmokeGrenadeTests
{
    /// <summary>The creation tick is separate from the 900-tick emission and 1080-tick lifetime clocks.</summary>
    [Fact]
    public void Smoke_stops_emitting_after_900_ticks_and_disappears_after_1080()
    {
        EmptyWorld query = new();
        Igi2PhysicsWorld physics = new(query, new Igi2GameMaterials());
        Recording effects = new();
        Igi2GrenadeThrow thrown = new(default, new float[9], default, 1, new Igi2WeaponTypeTask(), default, null);
        Igi2Grenade grenade = new(thrown, "smoke", null, Igi2GrenadeKind.Smoke);
        Assert.True(grenade.Run(physics, effects));
        Assert.Empty(effects.Sounds);
        Assert.True(grenade.Run(physics, effects));
        Assert.Equal(0, grenade.SmokeTicks);
        Assert.Equal("explo_smoke", Assert.Single(effects.Sounds));
        for (int tick = 1; tick <= 900; tick++)
        {
            Assert.True(grenade.Run(physics, effects));
        }

        Assert.Equal(6.0, effects.Plume.GenerateFactor);
        Assert.Equal(30, effects.Areas);
        Assert.True(grenade.Run(physics, effects));
        Assert.Equal(0.0, effects.Plume.GenerateFactor);
        for (int tick = 902; tick <= 1080; tick++)
        {
            Assert.True(grenade.Run(physics, effects));
        }

        Assert.Equal(36, effects.Areas);
        Assert.False(grenade.Run(physics, effects));
        Assert.Single(effects.Sounds);
    }

    /// <summary>A continuing smoke exposure does not overlap a cough, and a finished cough restarts only on renewed exposure.</summary>
    [Fact]
    public void Cough_requires_exposure_and_no_live_sound()
    {
        var (player, _, sounds) = Igi2PlayerFakes.OnFloor();
        player.SmokeArea(player.Position, 12288f);
        player.Update();
        Igi2PlayerFakes.FakeSound cough = Assert.Single(sounds.Started, static s => s.Name == "cough");
        player.SmokeArea(player.Position, 12288f);
        player.Update();
        Assert.Single(sounds.Started, static s => s.Name == "cough");
        cough.IsAlive = false;
        player.Update();
        Assert.Single(sounds.Started, static s => s.Name == "cough");
        player.SmokeArea(new Vector3d(player.Position.X + 12289.0, player.Position.Y, player.Position.Z), 12288f);
        player.Update();
        Assert.Single(sounds.Started, static s => s.Name == "cough");
        player.SmokeArea(player.Position, 12288f);
        player.Update();
        Assert.Equal(2, sounds.Started.Count(static s => s.Name == "cough"));
    }

    /// <summary>Grenade smoke uses the particle module's stream, preserving the gameplay random stream.</summary>
    [Fact]
    public void Smoke_uses_retail_plume_parameters_without_run_stream_draws()
    {
        EmptyWorld query = new();
        Igi2DynamicTasks tasks = new();
        IgiRandom random = new(123u), reference = new(123u);
        Igi2ExplosionWorld world = new(random, query, new Igi2GameMaterials(), tasks)
        {
            Particles = new Igi2Particles(query, tasks),
        };
        Igi2SmokeTask smoke = world.Smoke(default);
        Assert.Equal(new Vector3f(31.5f, 31.5f, 31.5f), smoke.System!.Color);
        Assert.Equal(4096f, smoke.System.Width);
        Assert.Equal(0.6f, smoke.SizeDelta);
        for (int i = 0; i < 6; i++)
        {
            smoke.Emit();
        }

        Assert.Equal(1, smoke.System.Count);
        Assert.Equal(reference.NextRaw(), random.NextRaw());
    }

    private sealed class Recording : IIgi2GrenadeEffects
    {
        public List<string> Sounds { get; } = [];
        public Igi2SmokeTask Plume { get; } = new() { GenerateFactor = 6.0 };
        public int Areas { get; private set; }
        public Igi2SmokeTask Smoke(Vector3d position) => Plume;
        public void SmokeArea(Vector3d position, float range)
        {
            Assert.Equal(12288f, range);
            Areas++;
        }

        public void Explode(in Igi2GrenadeExplosion explosion) => Assert.Fail("Smoke must not explode.");
        public void Sound(string sound, Vector3d position) => Sounds.Add(sound);
        public void AiEvent(int aiEvent, int flags, Vector3d position, float range) => Assert.Fail("Smoke has no flight AI event.");
    }

    private sealed class EmptyWorld : IIgi2CollisionQuery
    {
        public void NextZ(WorldLineTrace trace, ReadOnlySpan<double> distances, uint groups) => Clear(trace);
        public void TraceLines(WorldLineTrace trace, uint groups) => Clear(trace);
        private static void Clear(WorldLineTrace trace)
        {
            foreach (WorldIntersection hit in trace.Intersections)
            {
                hit.Clear();
            }
        }
    }
}
