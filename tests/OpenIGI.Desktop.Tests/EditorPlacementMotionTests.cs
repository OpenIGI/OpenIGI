using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>The editor's layer of live offsets over a title's own motion.</summary>
public sealed class EditorPlacementMotionTests
{
    /// <summary>
    /// An edited placement the title does not move is offset from where it was placed. A title's motion writes its out
    /// values even when it says no, and one that leaves them zeroed once sent every edited object to the world's origin.
    /// </summary>
    [Fact]
    public void An_offset_starts_from_the_placement_when_the_title_does_not_move_it()
    {
        WorldPlacement placement = Placement(new Vector3d(100, 200, 30));
        EditorPlacementMotion motion = new() { Inner = new SaysNo() };
        motion.SetPose(PlacementKey.Of(placement), new EditorPose(placement.Position, new Vector3d(0, 0, 5), Matrix3f.Identity));

        Assert.True(motion.TryGetTransform(placement, 0f, out Vector3d at, out Matrix3f turned));
        Assert.Equal(new Vector3d(100, 200, 35), at);
        Assert.Equal(Matrix3f.Identity, turned);
    }

    /// <summary>A placement the title does move is offset from where the title has it.</summary>
    [Fact]
    public void An_offset_rides_on_the_title_motion()
    {
        WorldPlacement placement = Placement(new Vector3d(100, 200, 30));
        EditorPlacementMotion motion = new() { Inner = new Lifts(10) };
        motion.SetPose(PlacementKey.Of(placement), new EditorPose(placement.Position, new Vector3d(1, 0, 0), Matrix3f.Identity));

        Assert.True(motion.TryGetTransform(placement, 0f, out Vector3d at, out _));
        Assert.Equal(new Vector3d(101, 200, 40), at);
    }

    /// <summary>A placement with no pose and no title motion is left alone.</summary>
    [Fact]
    public void An_untouched_placement_is_not_moved()
    {
        WorldPlacement placement = Placement(new Vector3d(1, 2, 3));
        Assert.False(new EditorPlacementMotion { Inner = new SaysNo() }.TryGetTransform(placement, 0f, out _, out _));
    }

    private static WorldPlacement Placement(Vector3d at) => new(
        Position: at,
        Orientation: Matrix3f.Identity,
        Instance: default,
        Pack: null!,
        Model: null!,
        TaskName: "Crate",
        Owner: null!);

    private sealed class SaysNo : IPlacementMotion
    {
        public bool TryGetTransform(WorldPlacement placement, float tickFraction, out Vector3d positionMetres, out Matrix3f orientation)
        {
            positionMetres = default;
            orientation = default;
            return false;
        }
    }

    private sealed class Lifts(double metres) : IPlacementMotion
    {
        public bool TryGetTransform(WorldPlacement placement, float tickFraction, out Vector3d positionMetres, out Matrix3f orientation)
        {
            positionMetres = placement.Position + new Vector3d(0, 0, metres);
            orientation = placement.Orientation;
            return true;
        }
    }
}
