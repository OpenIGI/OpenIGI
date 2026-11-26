using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>What a thing moved once a tick is drawn as between ticks.</summary>
public sealed class TickPoseHistoryTests
{
    /// <summary>Seen at consecutive ticks, it is drawn between the two; asked again within a tick, it stays put.</summary>
    [Fact]
    public void Consecutive_ticks_are_drawn_between()
    {
        TickPoseHistory<string> history = new();
        history.Present("plane", 10, new Vector3d(0.0, 0.0, 0.0), Matrix3f.Identity, 1f, 0);
        TickPose drawn = history.Present("plane", 11, new Vector3d(4.0, 0.0, 0.0), Matrix3f.Identity, 0.25f, 1);
        Assert.Equal(new Vector3d(1.0, 0.0, 0.0), drawn.Position);
        Assert.Equal(new Vector3d(3.0, 0.0, 0.0), history.Present("plane", 11, new Vector3d(4.0, 0.0, 0.0), Matrix3f.Identity, 0.75f, 2).Position);
    }

    /// <summary>One whose last pose was not the tick before has nothing to come from.</summary>
    [Fact]
    public void A_skipped_tick_draws_where_it_is()
    {
        TickPoseHistory<string> history = new();
        history.Present("car", 10, new Vector3d(0.0, 0.0, 0.0), Matrix3f.Identity, 1f, 0);
        Assert.Equal(new Vector3d(8.0, 0.0, 0.0), history.Present("car", 12, new Vector3d(8.0, 0.0, 0.0), Matrix3f.Identity, 0.5f, 1).Position);
    }

    /// <summary>Half way through a quarter turn, the drawn frame is still a rotation, and turned an eighth.</summary>
    [Fact]
    public void Orientation_stays_a_rotation_between_ticks()
    {
        TickPoseHistory<string> history = new();
        history.Present("rotor", 1, default, Matrix3f.Identity, 1f, 0);
        Matrix3f turned = history.Present("rotor", 2, default, Matrix3f.RotationZ(MathF.PI / 2f), 0.5f, 1).Orientation;
        Matrix3f eighth = Matrix3f.RotationZ(MathF.PI / 4f);
        Assert.Equal(1f, turned.Row1.Length, 4);
        Assert.Equal(1f, turned.Row2.Length, 4);
        Assert.Equal(eighth.M11, turned.M11, 4);
        Assert.Equal(eighth.M12, turned.M12, 4);
    }

    /// <summary>Each thing keeps its own serial from the first given, and one forgotten is gone.</summary>
    [Fact]
    public void Serials_are_distinct_and_forgetting_lets_go()
    {
        TickPoseHistory<string> history = new(firstSerial: 100);
        long a = history.Present("a", 1, default, Matrix3f.Identity, 1f, seen: 5).Serial;
        long b = history.Present("b", 1, default, Matrix3f.Identity, 1f, seen: 50).Serial;
        Assert.Equal(100, a);
        Assert.Equal(101, b);
        history.Forget(before: 10);
        Assert.Equal(1, history.Count);
    }
}
