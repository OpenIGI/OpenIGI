using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Flashbang range, facing, sight, fading and random draws from <c>flashbang.c</c> and the human handlers.</summary>
public sealed class Igi2FlashBangTests
{
    /// <summary>A directly facing player receives five times the distance falloff, regardless of team.</summary>
    [Fact]
    public void Player_flash_uses_facing_and_eye_distance_with_a_raised_sight_line()
    {
        var (player, _, _) = Igi2PlayerFakes.OnFloor();
        player.IsActive = true;
        Query query = new();
        Igi2DetectionWorld sight = new(query, new Igi2GameMaterials());
        Vector3d position = new(0.0, 61440.0, 0.0);
        player.FlashBang(new Igi2FlashBangHit(position, 122880f, player.Team, sight));
        // The 24-bit reciprocal before multiplying by the forward component rounds the five-times share down.
        Assert.Equal(2.4999998f, player.FlashBangFlash);
        Assert.Equal(new Vector3d(0.0, 61440.0, 409.6000061035156), query.From);
        Assert.Equal(player.View.Position, query.To);
        Assert.Equal(0xFFFFFFFFu, query.Groups);
        Assert.False(query.Includes!(player));
    }

    /// <summary>Behind, out of range, inactive or occluded flashes leave the previous flash unchanged.</summary>
    [Fact]
    public void Player_flash_requires_active_in_range_forward_and_visible()
    {
        var (player, _, _) = Igi2PlayerFakes.OnFloor();
        player.IsActive = true;
        player.FlashBangFlash = 0.25f;
        player.FlashBang(new Igi2FlashBangHit(new Vector3d(0.0, -1000.0, 0.0), 122880f, -1, null));
        Assert.Equal(0.25f, player.FlashBangFlash);
        player.FlashBang(new Igi2FlashBangHit(new Vector3d(0.0, 122880.0, 0.0), 122880f, -1, null));
        Assert.Equal(0.25f, player.FlashBangFlash);
        Query query = new() { Clear = false };
        player.FlashBang(new Igi2FlashBangHit(new Vector3d(0.0, 1000.0, 0.0), 122880f, -1,
            new Igi2DetectionWorld(query, new Igi2GameMaterials())));
        Assert.Equal(0.25f, player.FlashBangFlash);
        player.IsActive = false;
        player.FlashBang(new Igi2FlashBangHit(new Vector3d(0.0, 1000.0, 0.0), 122880f, -1, null));
        Assert.Equal(0.25f, player.FlashBangFlash);
    }

    /// <summary>The slow tail starts at exactly one and subtraction clamps to zero.</summary>
    [Theory]
    [InlineData(2f, 1.9833332f)]
    [InlineData(1f, 0.9916666f)]
    [InlineData(0.001f, 0f)]
    public void Player_flash_fades_each_update(float initial, float expected)
    {
        var (player, _, _) = Igi2PlayerFakes.OnFloor();
        player.FlashBangFlash = initial;
        player.Update();
        Assert.Equal(expected, player.FlashBangFlash);
    }

    /// <summary>The soldier includes the radius boundary and side plane and sends the event only to its own mind.</summary>
    [Fact]
    public void Soldier_flash_sends_event_18_with_the_throwers_team()
    {
        var (_, animation, surroundings) = Igi2PlayerFakes.OnFloor();
        RecordingMind mind = new();
        Igi2HumanSoldier soldier = new(new Igi2NoWeaponControl(), animation, surroundings) { Mind = mind };
        Vector3d position = new(122880.0, 0.0, 0.0);
        soldier.FlashBang(new Igi2FlashBangHit(position, 122880f, 2, null));
        Igi2AiEvent heard = Assert.Single(mind.Events);
        Assert.Equal(18, heard.Event);
        Assert.Equal(67, heard.Flags);
        Assert.Equal(2, heard.Team);
        Assert.Equal(position, heard.Position);
        Assert.Equal(122880f, heard.Radius);
        Assert.Null(heard.Originator);
        Assert.Null(heard.Reference);
        soldier.FlashBang(new Igi2FlashBangHit(new Vector3d(0.0, -1.0, 0.0), 122880f, -1, null));
        soldier.FlashBang(new Igi2FlashBangHit(new Vector3d(122881.0, 0.0, 0.0), 122880f, -1, null));
        Assert.Single(mind.Events);
    }

    /// <summary>Human dispatch is immediate, whereas the puff draws nine run values and sounds on its run.</summary>
    [Fact]
    public void Flash_puff_draws_nine_run_values_and_sounds_once()
    {
        Query query = new();
        Igi2DynamicTasks dynamic = new();
        IgiRandom random = new(123u), reference = new(123u);
        RecordingEffects effects = new();
        var (player, _, _) = Igi2PlayerFakes.OnFloor();
        player.IsActive = true;
        Igi2ExplosionWorld world = new(random, query, new Igi2GameMaterials(), dynamic)
        {
            Particles = new Igi2Particles(query, dynamic), Humans = [player], Effects = effects,
        };
        Igi2GrenadeThrow thrown = new(new Vector3d(0.0, 61440.0, 0.0), new float[9], default, 0,
            new Igi2WeaponTypeTask(), default, player);
        Igi2Grenade grenade = new(thrown, "flash", null, Igi2GrenadeKind.Flashbang);
        Assert.False(grenade.Run(new Igi2PhysicsWorld(query, new Igi2GameMaterials()), world));
        Assert.Equal(2.4999998f, player.FlashBangFlash);
        Assert.Empty(effects.Sounds);
        Igi2FlashBang flash = Assert.Single(dynamic.Tasks.OfType<Igi2FlashBang>());
        Assert.Equal(0, flash.System!.Count);
        Assert.Equal(0x1040008, flash.System.Look.Transparency);
        Assert.True(flash.System.IsSelfDestruct);
        dynamic.Run();
        Assert.Equal("explo_flash", Assert.Single(effects.Sounds));
        Assert.Equal(3, flash.System.Count);
        Assert.All(flash.System.Particles, static p =>
        {
            Assert.InRange(p.Intensity, 0.25f, 0.5f);
            Assert.InRange(p.Speed.Z, 56f, 114f);
            Assert.Equal(89, p.LifeTime);
        });
        for (int i = 0; i < 9; i++)
        {
            reference.NextRaw();
        }

        Assert.Equal(reference.NextRaw(), random.NextRaw());
        Assert.DoesNotContain(dynamic.Tasks, static t => t is Igi2FlashBang);
    }

    private sealed class RecordingMind : IIgi2SoldierMind
    {
        public List<Igi2AiEvent> Events { get; } = [];
        public bool HasStarted => true;
        public void Run(Igi2HumanSoldier soldier) { }
        public void Perceive(Igi2HumanSoldier soldier, in Igi2AiEvent aiEvent) => Events.Add(aiEvent);
    }

    private sealed class RecordingEffects : IIgi2ExplosionEffects
    {
        public List<string> Sounds { get; } = [];
        public void BurnMark(WorldIntersection ground, float radius) { }
        public void Sound(string sound, Vector3d position) => Sounds.Add(sound);
        public void AiEvent(int aiEvent, int flags, Vector3d position, float range, int team = 1) { }
    }

    private sealed class Query : IIgi2CollisionQuery, IIgi2SightQuery
    {
        public bool Clear { get; set; } = true;
        public Vector3d From { get; private set; }
        public Vector3d To { get; private set; }
        public uint Groups { get; private set; }
        public Func<object, bool>? Includes { get; private set; }
        public void NextZ(WorldLineTrace trace, ReadOnlySpan<double> distances, uint groups) { }
        public void TraceLines(WorldLineTrace trace, uint groups) { }
        public bool LineOfSight(WorldLineOfSight sight, uint groups)
        {
            From = sight.Start;
            To = sight.End;
            Groups = groups;
            Includes = sight.Includes;
            return Clear;
        }
    }
}
