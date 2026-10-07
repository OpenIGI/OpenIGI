using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The ported player standing and walking in mission 1, over the level's own collision and the retail tuning.</summary>
public sealed class RetailIgi2GameplayTests
{
    private static readonly Lazy<Igi2HumanPlayerGeneral> Tuning = new(LoadTuning);

    private static readonly Lazy<Igi2CollisionWorld> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        return Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
    });

    /// <summary>The session reads the player's tuning from <c>humanplayer.qvm</c> when its tables load.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_session_reads_the_player_tuning()
    {
        Assert.Equal(834.3703002929688f, Tuning.Value.UpwardJumpSpeed);
        Assert.Equal(7, Tuning.Value.PeekTime);
    }

    /// <summary>Spawned where mission 1 puts the player, it stands on the ground and stays there.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_stands_where_the_level_puts_it()
    {
        Igi2TitlePlayer player = Spawned(out Vector3d spawn);

        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(Idle);
        }

        Assert.True((player.Human.Signal & Igi2PlayerSignal.Grounded) != 0, "the player is not on the ground.");
        Assert.InRange(player.Position.X, spawn.X - 1.0, spawn.X + 1.0);
        Assert.InRange(player.Position.Y, spawn.Y - 1.0, spawn.Y + 1.0);
        Assert.Equal(Igi2Human.EyesAt(player.Position, Igi2HumanPose.Standing), player.EyePosition);
    }

    /// <summary>Holding forward, the player runs along its heading and stays on the ground.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_runs_forward()
    {
        Igi2TitlePlayer player = Spawned(out Vector3d spawn);
        for (int tick = 0; tick < 5; tick++)
        {
            player.Tick(Idle);
        }

        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(Idle with { Held = PlayerAction.MoveUp });
        }

        double dx = player.Position.X - spawn.X, dy = player.Position.Y - spawn.Y;
        Assert.True(Math.Sqrt((dx * dx) + (dy * dy)) > 4096.0, $"the player only reached {player.Position} from {spawn}.");
        Assert.True((player.Human.Signal & Igi2PlayerSignal.Grounded) != 0, "the player left the ground.");
    }

    /// <summary>The mouse turns the view; the camera's rows are the view's right, up and forward axes.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_mouse_turns_the_view()
    {
        Igi2TitlePlayer player = Spawned(out _);
        player.Tick(Idle);
        float before = player.Human.Gamma;

        player.Tick(Idle with { MouseX = 20f });

        Assert.NotEqual(before, player.Human.Gamma);
        Matrix3f view = player.ViewOrientation;
        Assert.Equal(player.Human.View.Orientation.M12, view.M31);
        Assert.Equal(player.Human.View.Orientation.M13, view.M21);
    }

    private static TitlePlayerInput Idle => new(0f, 0f, default, default, 0.5f, false);

    private static Igi2TitlePlayer Spawned(out Vector3d spawn)
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        spawn = point.Position;
        return Igi2TitlePlayer.Spawn(Mission1.Value, Tuning.Value, point.Position, point.Gamma, AiDifficulty.Easy);
    }

    private static Igi2HumanPlayerGeneral LoadTuning()
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        ResourceRegistry resources = new(files);
        GameLoop loop = new(new ManualTimeSource(1_000), structuralTypes: Igi2Runtime.Instance.Layout.StructuralTaskTypes);
        TitleSession session = TitleSession.Start(Igi2Runtime.Instance.Modules, loop, files, resources, loadTables: true);
        return session.Services.Require<Igi2PlayerTuning>().General;
    }
}
