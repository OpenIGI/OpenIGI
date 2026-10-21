using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A human as any game's AI perceives it: the engine's <c>Human</c>, its body the one its game keeps.</summary>
public sealed class HumanAiContractTests
{
    [Fact]
    public void Without_a_body_a_human_stands_where_its_parameters_put_it()
    {
        HumanSoldierTask soldier = new() { Position = new Vector3d(10, 20, 30), Team = 2 };
        IAiHuman human = soldier;

        Assert.Equal(new Vector3d(10, 20, 30), human.Position);
        Assert.Equal(30 + PlayerBody.StandingEyeHeight, human.OriginalViewPosition.Z);
        AiDetectionInfo info = soldier.DetectionInfo();
        Assert.Equal((AiDetectableKind.Human, 2, true, true), (info.Kind, info.Team, info.IsAlive, info.IsActive));
    }

    [Fact]
    public void A_human_is_seen_at_one_point_its_eyes_as_its_body_says()
    {
        // 0x45FE30: the kind, the side, alive unless the death latch is set, active as the body says, and one point (0x465820).
        HumanSoldierTask soldier = new() { Team = 1 };
        Body body = new(new Vector3d(100, 200, 300), new Vector3d(100, 200, 7000), keepingLow: true, active: false);
        soldier.Body = body;

        IAiHuman human = soldier;
        Assert.Equal(body.Feet, human.Position);
        Assert.Equal(body.Eyes, human.OriginalViewPosition);
        AiDetectionInfo info = soldier.DetectionInfo();
        Assert.Equal((body.Eyes, body.Eyes, body.Eyes), (info.Point(0), info.Point(1), info.Point(2)));
        Assert.False(info.IsActive);

        soldier.IsDead = true;
        Assert.False(soldier.DetectionInfo().IsAlive);
        Assert.True(human.IsDead);
    }

    [Fact]
    public void The_player_keeps_low_as_its_body_says_and_counts_its_spottings()
    {
        HumanPlayerTask player = new();
        Assert.False(player.IsKeepingLow);

        player.Body = new Body(default, default, keepingLow: true, active: true);
        Assert.True(player.IsKeepingLow);

        player.NoteDetected();
        player.NoteDetected();
        Assert.Equal(2, player.TimesSpotted);
    }

    private sealed class Body(Vector3d feet, Vector3d eyes, bool keepingLow, bool active) : IHumanBodyState
    {
        public Vector3d Feet => feet;

        public Vector3d Eyes => eyes;

        public bool IsKeepingLow => keepingLow;

        public bool IsActive => active;
    }
}
