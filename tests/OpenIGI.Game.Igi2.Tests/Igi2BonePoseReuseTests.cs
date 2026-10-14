using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Unchanged bone palettes keep their immutable pose; changed palettes produce independent snapshots.</summary>
public sealed class Igi2BonePoseReuseTests
{
    /// <summary>Palette and hierarchy changes invalidate the cache without modifying an earlier pose.</summary>
    [Fact]
    public void Pose_cache_checks_bones_and_parents_and_preserves_previous_snapshots()
    {
        Igi2BoneBuffer bones = new(2);
        int[] parents = [-1, 0];
        var first = bones.ToPose(parents);
        Assert.Same(first, bones.ToPose(parents));
        bones.Translations[1] = new Vector3f(1f, 2f, 3f);
        var moved = bones.ToPose(parents);
        Assert.NotSame(first, moved);
        Assert.Equal(default, first.Positions[1]);
        Assert.Equal(bones.Translations[1], moved.Positions[1]);
        Assert.Same(moved, bones.ToPose(parents));
        bones.Matrices[1] = Matrix3f.FromRows(Vector3f.UnitY, -Vector3f.UnitX, Vector3f.UnitZ);
        var rotated = bones.ToPose(parents);
        Assert.NotSame(moved, rotated);
        Assert.Equal(Matrix3f.Identity, moved.Rotations[1]);
        parents[1] = -1;
        var reparented = bones.ToPose(parents);
        Assert.NotSame(rotated, reparented);
        Assert.Equal(0, rotated.Parents[1]);
        Assert.Equal(-1, reparented.Parents[1]);
    }
}
