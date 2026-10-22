using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// An explosion going off, <c>Explosion0</c>: the run-stream draws it makes (<c>Explosion0_New</c>, <c>igi2 0x50A4E0</c>, and
/// what it leaves), the damage it does by distance (<c>GameFunctions_ResolveExplosionHit</c>, <c>0x48D1F0</c>) and the player
/// it reaches (<c>HumanPlayer_ExplosionHitHandler</c>, <c>0x4BA330</c>).
/// </summary>
public sealed class Igi2ExplosionTests
{
    private const uint Seed = 0x1234567u;

    /// <summary>
    /// A grenade's blast draws three for its fragments' direction and three for their spin when it starts; going off, three
    /// fragments draw seven each; its smoke's first tick draws four, and its black puff two more seven ticks later: at nine
    /// tenths of sixty, which the 24-bit FPU makes 53.999998 and the truncating store 53.
    /// </summary>
    [Fact]
    public void A_grenade_blast_draws_from_the_run_stream_as_retail_does()
    {
        IgiRandom stream = new(Seed);
        IgiRandom reference = new(Seed);
        Igi2DynamicTasks dynamic = new();
        Igi2ExplosionWorld world = new(stream, new OpenGround(), new Igi2GameMaterials(), dynamic);

        world.Explode(Grenade(new Vector3d(0.0, 0.0, 0.0)));
        Assert.True(InStep(stream, reference, 6));

        dynamic.Run();
        Assert.Contains(dynamic.Tasks, static t => t is Igi2ExplosionSmoke);
        Assert.Contains(dynamic.Tasks, static t => t is Igi2FireBall);
        Igi2FragmentContainer fragments = Assert.Single(dynamic.Tasks.OfType<Igi2FragmentContainer>());
        Assert.Equal(3, fragments.Flying.Count);
        Assert.DoesNotContain(dynamic.Tasks, static t => t is Igi2Explosion);
        Assert.True(InStep(stream, reference, 21 + 4));

        for (int i = 0; i < 6; i++)
        {
            dynamic.Run();
        }

        Assert.True(InStep(stream, reference, 0));
        dynamic.Run();
        Assert.True(InStep(stream, reference, 2));
    }

    /// <summary>
    /// Through nothing, a blast does its full damage within its full range of the target's edge, falls away as the square of
    /// the share of the falloff left, and does nothing past it.
    /// </summary>
    [Fact]
    public void A_blast_falls_away_past_its_full_range()
    {
        Igi2ExplosionWorld world = new(new IgiRandom(Seed), new OpenGround(), new Igi2GameMaterials(), new Igi2DynamicTasks());
        Igi2ExplosionHit hit = new(world, new Vector3d(0.0, 0.0, 0.0), -1, 4f, 16384f, 40960f, 90, null, null, Igi2ExplosionFalloff.Exponential);

        Assert.Equal(4.0, hit.Resolve(new Vector3d(10000.0, 0.0, 0.0), 0f).Value);
        Assert.Equal(1.0, hit.Resolve(new Vector3d(16384.0 + 20480.0, 0.0, 0.0), 0f).Value, 5);
        Assert.Equal(4.0, hit.Resolve(new Vector3d(16384.0 + 1000.0, 0.0, 0.0), 1000f).Value);
        Assert.Equal(0.0, hit.Resolve(new Vector3d(16384.0 + 40960.0 + 1.0, 0.0, 0.0), 0f).Value);
    }

    /// <summary>
    /// A grenade going off two metres from the player hurts its chest by the whole damage, shakes the view fully, makes it cry
    /// out and flashes the view red.
    /// </summary>
    [Fact]
    public void A_grenade_blast_hurts_and_shakes_the_player()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.OnFloor();
        player.General = player.General with
        {
            MaxDamageScale = 100f,
            DamageMultipliers = [.. Enumerable.Repeat(1f, Igi2HumanDamage.PartCount)],
        };
        player.Create(OpenIGI.Game.Ai.AiDifficulty.Normal);
        player.UpdateViewBody(updateAnimation: false);
        Igi2DynamicTasks dynamic = new();
        Igi2ExplosionWorld world = new(player.Random, new OpenGround(), new Igi2GameMaterials(), dynamic);
        world.AddTarget(player);

        world.Explode(Grenade(new Vector3d(player.Position.X + 8192.0, player.Position.Y, player.Position.Z)));
        dynamic.Run();

        Assert.Equal(4f, player.Damage.PartDamage(Igi2DamagePart.Chest));
        Assert.Equal(4f, player.Damage.Damage);
        Assert.Equal(1f, player.View.ShakeFactor);
        Assert.Equal(204.8f, player.View.ShakeRange);
        Assert.Contains(surroundings.Started, static s => Igi2HumanPlayer.ExplosionHitSounds.Contains(s.Name));
        Assert.All(player.DamageFlash, static f => Assert.InRange(f, 0.4f, 0.8f));
    }

    private static Igi2GrenadeExplosion Grenade(Vector3d at) =>
        new(at, new WorldIntersection(), 4f, 16384f, 40960f, null);

    /// <summary>
    /// Whether the stream has drawn as many more times as the reference is stepped now: both then draw once more, which keeps
    /// them in step for the next check.
    /// </summary>
    private static bool InStep(IgiRandom stream, IgiRandom reference, int draws)
    {
        for (int i = 0; i < draws; i++)
        {
            _ = reference.NextRaw();
        }

        return stream.NextRaw() == reference.NextRaw();
    }

    /// <summary>A world with nothing in it: every line is clear.</summary>
    private sealed class OpenGround : IIgi2CollisionQuery
    {
        public void NextZ(WorldLineTrace trace, ReadOnlySpan<double> distances, uint groups)
        {
            for (int i = 0; i < trace.Count; i++)
            {
                trace.Intersections[i].Clear();
            }
        }

        public void TraceLines(WorldLineTrace trace, uint groups)
        {
            for (int i = 0; i < trace.Count; i++)
            {
                trace.Intersections[i].Clear();
            }
        }
    }
}
