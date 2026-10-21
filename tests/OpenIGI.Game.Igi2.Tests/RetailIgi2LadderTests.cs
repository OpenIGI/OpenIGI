using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Ladders, as ladder.c places them and humanplayer.c climbs them.</summary>
public sealed class RetailIgi2LadderTests
{
    /// <summary>Mission 1's buildings carry ladders on their attach points, each with steps between its foot and its top.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1s_buildings_carry_ladders()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2LadderTask[] ladders = Ladders(m);
        Assert.NotEmpty(ladders);
        Assert.All(ladders, static l => Assert.True(l.Steps > 6, $"{l.Model} has {l.Steps} steps"));
    }

    /// <summary>
    /// At a ladder's foot, facing it, the player that presses activate gets on it at the bottom step, standing for the third camera
    /// mode with the collision off; holding forward it climbs, and each step's end counts a step.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_climbs_a_ladder_from_its_foot()
    {
        (Igi2TitlePlayer player, Igi2LadderTask ladder) = AtLadderFoot();
        Assert.Same(ladder, player.Human.EvaluateTarget);
        Assert.False(player.Human.IsLadderActivateTop);
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate, Pressed = PlayerAction.Activate });
        Assert.True(player.Human.IsClimbLadder);
        Assert.Equal(Igi2PlayerState.ClimbLadder, player.Human.Locomotion.CurrentState);
        Assert.True(player.Human.IsDisableCollision);
        Assert.Equal(3, player.Human.CameraMode);
        Assert.Equal(ladder.Steps, player.Human.LadderSteps);
        Assert.Same(ladder, player.Human.AttachParent);
        int firstStep = player.Human.LadderStep;
        double startZ = player.Human.Position.Z;

        for (int tick = 0; tick < 90; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.MoveUp });
        }

        Assert.True(player.Human.Position.Z > startZ + 1024.0, $"climbed from {startZ} to {player.Human.Position.Z}");
        Assert.True(player.Human.LadderStep > firstStep);
    }

    /// <summary>
    /// At the top of the ladder the player gets off: from six steps short of its top, forward gets off over its top, and at the clip's
    /// end the player stands 1.5 m on and 2 m up, off the ladder, its collision and camera back.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_gets_off_at_the_top()
    {
        (Igi2TitlePlayer player, Igi2LadderTask ladder) = AtLadderFoot();
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate, Pressed = PlayerAction.Activate });
        bool wasOffTop = false;
        for (int tick = 0; tick < 4000 && (player.Human.IsClimbLadder || !wasOffTop); tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.MoveUp });
            wasOffTop |= player.Human.Locomotion.CurrentState == Igi2PlayerState.ClimbLadderGetOffTop;
        }

        Assert.True(wasOffTop);
        Assert.False(player.Human.IsClimbLadder);
        Assert.Null(player.Human.AttachParent);
        Assert.False(player.Human.IsDisableCollision);
        Assert.Equal(1, player.Human.CameraMode);
        Assert.True(player.Human.Position.Z > ladder.MagicVertex(3).Z - 4096.0);
    }

    // The player at a ladder of mission 1's, half a metre before its foot, facing it, armed, settled for two seconds.
    private static (Igi2TitlePlayer Player, Igi2LadderTask Ladder) AtLadderFoot()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2LadderTask ladder = Ladders(m)[0];
        IReadOnlyList<float> f = ladder.Frame;
        Vector3d ahead = new(f[1], f[4], f[7]);
        Vector3d foot = ladder.MagicVertex(2);
        Vector3d at = foot + (ahead * 2048.0);
        float facing = MathF.Atan2((float)ahead.X, -(float)ahead.Y);

        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = m.Level.ModelFor(point.Model)!;
        Igi2Session session = Igi2Session.Start();
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, m.Level.Root, tuning, clips)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(
            m.Collision!, tuning.General, new Vector3d(at.X, at.Y, foot.Z + 4096.0), facing,
            AiDifficulty.Easy, clips, body.BoneHierarchy, weapons, sounds: m.Sounds);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        player.Human.AnimTriggers = m.Services!.Require<Igi2AnimTriggers>();
        player.Activatables.Add(ladder);
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle);
        }

        return (player, ladder);
    }

    private static Igi2LadderTask[] Ladders(RetailIgi2Mission1 m)
    {
        Igi2MagicObjTypes magic = m.Services!.Require<Igi2MagicObjTypes>();
        return
        [
            .. Igi2CollisionWorld.MagicObjects(m.Level.Scene, m.Level.ModelFor, magic)
                .Where(static p => p.Type.TaskType == Igi2LadderTask.TypeName)
                .Select(p => Igi2LadderTask.FromAttachPoint(p, m.Level.ModelFor)),
        ];
    }
}
