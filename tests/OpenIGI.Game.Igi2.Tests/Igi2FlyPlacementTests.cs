using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Cameras;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>F1 return placement uses the fly camera's location, new footing and a fresh presentation history.</summary>
public sealed class Igi2FlyPlacementTests
{
    private static readonly Lazy<Igi2Level> Level = new(() => Igi2Level.Find("location1", "level1"));
    private static readonly Lazy<Igi2CollisionWorld> World = new(() => Igi2CollisionWorld.Build(
        Level.Value.Scene, Level.Value.ModelFor, Level.Value.Terrain(), "terrain"));

    /// <summary>No ground leaves the old player and pending aim unchanged, so the host can remain flying.</summary>
    [Fact]
    public void Missing_ground_preserves_the_player()
    {
        Vector3d original = new(1000.0, 2000.0, 3000.0);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
            Igi2HumanPlayerGeneral.Unset with { MaxDamageScale = 1f }, original, 0.5f, AiDifficulty.Normal);
        player.Human.Speed = new(1f, 2f, 3f);
        player.GatherLook(8f, 0f, 0.4f, false);
        Matrix3f aim = player.PreviewLook(0f, 0f, 0.4f, false);
        Assert.False(player.TryPlace(new(40000.0, 50000.0, 60000.0), -1f));
        Assert.Equal(original, player.Position);
        Assert.Equal(0.5f, player.Human.Gamma);
        Assert.Equal(new Vector3f(1f, 2f, 3f), player.Human.Speed);
        Assert.Equal(aim, player.PreviewLook(0f, 0f, 0.4f, false));
    }

    /// <summary>Returning from flight moves feet and eyes immediately, drops old movement, and faces the fly heading.</summary>
    [RequiresTitleDataTheory("igi2")]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(-1.2f)]
    public void Returning_places_the_player_below_the_camera(float yaw)
    {
        Assert.True(PlayerSpawn.TryFindGameplay(Level.Value.Root, out PlayerSpawnPoint spawn));
        Igi2TitlePlayer player = Player(spawn.Position);
        TitlePlayerPresentation presentation = new();
        presentation.Reset(player);
        Vector3d camera = new(spawn.Position.X + 40960.0, spawn.Position.Y + 40960.0, spawn.Position.Z + 409600.0);
        player.Human.Speed = new(20f, 30f, -400f);
        player.Human.AccumulatedSpeed = new(40f, 50f, 60f);
        player.Human.ImpactSpeed = new(1f, 2f, 3f);
        player.Human.GroundMovementSpeed = new(100f, 200f, 0f);
        player.Human.View.IdealPitch = 0.2f;
        player.Human.View.ImpactOffset = 100f;
        player.Human.View.ShakeFactor = 1f;
        player.GatherLook(200f, 100f, 0.4f, false);
        Assert.True(player.TryPlace(camera, yaw));
        Assert.Equal(camera.X, player.Position.X);
        Assert.Equal(camera.Y, player.Position.Y);
        Assert.InRange(player.Position.Z, camera.Z - PlayerBody.PlacementSearchUnits, camera.Z);
        Assert.True(player.Human.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        Assert.NotNull(player.Human.GroundCollider);
        Assert.Equal(default, player.Human.Speed);
        Assert.Equal(default, player.Human.AccumulatedSpeed);
        Assert.Equal(default, player.Human.ImpactSpeed);
        Assert.Equal(default, player.Human.GroundMovementSpeed);
        Assert.Equal(player.Position, player.Human.WorldPreviousPosition);
        Assert.Equal(yaw, MathF.Atan2(player.ViewOrientation.Row3.X, player.ViewOrientation.Row3.Y), 5);
        Assert.Equal(player.LookOrientation, player.PreviewLook(0f, 0f, 0.4f, false));
        Assert.Equal(0f, player.Human.View.ImpactOffset);
        Assert.Equal(0f, player.Human.View.ShakeFactor);
        presentation.Reset(player);
        foreach (float alpha in new[] { 0f, 0.5f, 1f })
        {
            Assert.Equal(player.EyePosition * (1.0 / 4096.0), presentation.Camera(alpha).Position);
        }
        Vector3d feet = player.Position;
        player.Tick(new(0f, 0f, default, default, 0.4f, false, UnfilteredMouse: true));
        Assert.InRange((player.Position - feet).Length, 0.0, 1.0);
        Assert.Equal(yaw, MathF.Atan2(player.ViewOrientation.Row3.X, player.ViewOrientation.Row3.Y), 5);
    }

    /// <summary>A roof or platform replaces old ground and retains its correct relative frame on the following tick.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Returning_acquires_the_building_floor()
    {
        Igi2TitlePlayer player = Player(default);
        foreach (Igi2RigidObject building in World.Value.Objects.Where(o => o.Owner is Igi2CollisionOwner { TypeName: "Building", Depth: 0 }))
        {
            if (player.TryPlace(new(building.Position.X, building.Position.Y, building.Position.Z + 32768.0), 0.4f)
                && player.Human.IsGroundRigidObject) { break; }
        }
        Assert.True(player.Human.IsGroundRigidObject);
        Vector3d feet = player.Position;
        player.Tick(new(0f, 0f, default, default, 0.4f, false));
        Assert.InRange((player.Position - feet).Length, 0.0, 1.0);
        Assert.True(player.Human.IsGroundRigidObject);
    }

    private static Igi2TitlePlayer Player(Vector3d at) => Igi2TitlePlayer.Spawn(World.Value,
        Igi2Session.Start().Require<Igi2PlayerTuning>().General, at, 0f, AiDifficulty.Normal);
}
