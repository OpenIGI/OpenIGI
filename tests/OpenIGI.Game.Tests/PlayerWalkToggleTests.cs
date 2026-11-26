using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The walk/run toggle as <c>HumanPlayer_Update</c> (<c>0x4109A0</c>) takes it.</summary>
public sealed class PlayerWalkToggleTests
{
    /// <summary>
    /// The body's machine builds its walking bit from the latch (<c>0x4105C0</c>) and runs before the press flips it, so a press
    /// shows on the next tick's gait: the run goes on through the tick it is pressed in, and the walk starts the tick after.
    /// </summary>
    [RequiresGameDataFact]
    public void A_press_of_the_toggle_changes_the_gait_on_the_next_tick()
    {
        PlayerCamera rig = new();
        rig.UseAnimations(BoneAnimationSet.Read(File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff"))));
        rig.UseWalls(ObjectCollisionIndex.Build([]));
        rig.StandAfresh(default, 0f, 0f);
        Flat ground = new();
        for (int tick = 0; tick < 10; tick++)
        {
            rig.Tick(PlayerAction.MoveUp, ground);
        }

        Assert.Equal(1, rig.LocomotionState);

        rig.Tick(PlayerAction.MoveUp | PlayerAction.ToggleWalkRun, ground);
        Assert.Equal(1, rig.LocomotionState);

        rig.Tick(PlayerAction.MoveUp, ground);
        Assert.Equal(1 + HumanLocomotionStates.WalkingOffset, rig.LocomotionState);
    }

    private sealed class Flat : IGroundHeightField
    {
        public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
        {
            ground = new TerrainGround(0.0, 0.0, 0.0, 1.0);
            return true;
        }

        public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
        {
            ground = default;
            return false;
        }
    }
}
