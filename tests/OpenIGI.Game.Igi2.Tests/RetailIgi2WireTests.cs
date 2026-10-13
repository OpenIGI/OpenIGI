using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Wires, as wire.c strings them and humanplayer.c climbs and rides them.</summary>
public sealed class RetailIgi2WireTests
{
    /// <summary>
    /// The campaign's wires are strung between their ends, their magic vertices stretched along them; the sloping ones are ridden,
    /// mission 23's level one climbed.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Wires_are_strung_between_their_ends()
    {
        Igi2WireTask level = Wire("location2", "level3", 3428, out _);
        Assert.False(level.IsSliding);
        Igi2WireTask sloping = Wire("location2", "level2", 3550, out _);
        Assert.True(sloping.IsSliding);
        double length = (sloping.Stop - sloping.Start).Length;
        Assert.Equal(length, sloping.LocalMagicVertex(1).Y - sloping.LocalMagicVertex(0).Y, 0.01 * length);
    }

    /// <summary>
    /// A sloping wire, activated, hangs the player under it facing down it, collision off, riding with <c>wire_slide_1</c>; the player
    /// gathers speed down it and lets go three quarters of a metre short of its low end, falling, the wire no longer in use.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_rides_a_sloping_wire_down()
    {
        Igi2WireTask wire = Wire("location2", "level2", 3550, out Igi2Level level);
        Igi2TitlePlayer player = Armed(level, wire);
        player.Human.ScriptActivate(wire);
        player.Tick(RetailIgi2Mission1.Idle);
        Assert.True(player.Human.IsSlideWire);
        Assert.Equal(Igi2PlayerState.SlideWire, player.Human.Locomotion.CurrentState);
        Assert.True(player.Human.IsDisableCollision);
        Assert.True(wire.IsInUse);
        double startZ = player.Human.Position.Z;
        float lastSpeed = 0f;
        for (int tick = 0; tick < 3000 && player.Human.IsSlideWire; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle);
            lastSpeed = Math.Max(lastSpeed, player.Human.SlideWireSpeed);
        }

        Assert.False(player.Human.IsSlideWire);
        Assert.False(wire.IsInUse);
        Assert.Equal(Igi2PlayerState.Fall, player.Human.Locomotion.CurrentState);
        Assert.True(player.Human.Position.Z < startZ - 1024.0, $"rode from {startZ} to {player.Human.Position.Z}");
        Assert.True(lastSpeed > 0f);
    }

    /// <summary>A level wire, activated, is climbed: forward carries the player along it a step at a time, to its end, where it lets go.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_climbs_a_level_wire()
    {
        Igi2WireTask wire = Wire("location2", "level3", 3428, out Igi2Level level);
        Igi2TitlePlayer player = Armed(level, wire);
        player.Human.ScriptActivate(wire);
        player.Tick(RetailIgi2Mission1.Idle);
        Assert.True(player.Human.IsClimbWire);
        Assert.Equal(Igi2PlayerState.ClimbWire, player.Human.Locomotion.CurrentState);
        Vector3d start = player.Human.Position;
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.MoveUp });
        }

        Assert.True((player.Human.Position - start).Length > 2048.0, $"moved {(player.Human.Position - start).Length}");
        for (int tick = 0; tick < 6000 && player.Human.IsClimbWire; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.MoveUp });
        }

        Assert.False(player.Human.IsClimbWire);
        Assert.False(player.Human.IsDisableCollision);
    }

    private static Igi2WireTask Wire(string location, string levelName, int id, out Igi2Level level)
    {
        level = Igi2Level.Find(location, levelName);
        Igi2WireTask wire = All(level.Root).OfType<Igi2WireTask>().Single(w => w.Id == id);
        wire.Models = level.ModelFor;
        wire.Prepare();
        return wire;
    }

    // The level's player, armed, standing below the wire's middle.
    private static Igi2TitlePlayer Armed(Igi2Level level, Igi2WireTask wire)
    {
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = level.ModelFor(point.Model)!;
        Igi2Session session = Igi2Session.Start();
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, level.Root, tuning, clips)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, tuning.General, wire.Position, 0f, AiDifficulty.Easy, clips, body.BoneHierarchy, weapons);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        return player;
    }

    private static IEnumerable<GameTask> All(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask inner in All(child))
            {
                yield return inner;
            }
        }
    }
}
