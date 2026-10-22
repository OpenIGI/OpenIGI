using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>A Project I.G.I. human as another game's world traces it: in once, out once, as its own humans are.</summary>
public sealed class Igi1HumanLineColliderTests
{
    private const double Metre = 4096.0;

    // The world's human group and material row, as a world hosting the human names them.
    private const uint Group = 0x4000;
    private const int Material = 21;

    [Fact]
    public void A_round_goes_into_the_body_once_and_out_of_it_once()
    {
        Igi1HumanLineCollider body = new(new HumanPlayerTask { Body = new Standing(Vector3d.Zero) }, Group, Material);
        double radius = HumanHitTest.BodyRadiusInUnits;

        // From in front: the face it enters by.
        WorldIntersection inward = Trace(body, new Vector3d(-5.0 * Metre, 0.0, Metre), new Vector3d(5.0 * Metre, 0.0, Metre));
        Assert.True(inward.IsHit);
        Assert.Equal(Material, inward.EnterMaterial);
        Assert.Null(inward.LeaveMaterial);
        Assert.Equal(-radius, inward.Position.X, 3);

        // On from just past that point, as an I.G.I. 2 round traces on: the face it leaves by, not another way in.
        WorldIntersection outward = Trace(body, new Vector3d(-radius + 1.0, 0.0, Metre), new Vector3d(5.0 * Metre, 0.0, Metre));
        Assert.True(outward.IsHit);
        Assert.Null(outward.EnterMaterial);
        Assert.Equal(Material, outward.LeaveMaterial);
        Assert.Equal(radius, outward.Position.X, 3);
        Assert.Equal((2.0 * radius) - 1.0, outward.Distance, 3);
        Assert.True(outward.Normal.X > 0f);

        // A line that stays inside crosses nothing.
        Assert.False(Trace(body, new Vector3d(-0.1 * Metre, 0.0, Metre), new Vector3d(0.1 * Metre, 0.0, Metre)).IsHit);
    }

    private static WorldIntersection Trace(Igi1HumanLineCollider body, Vector3d start, Vector3d end)
    {
        WorldLineTrace trace = new(1);
        trace.Starts[0] = start;
        trace.Ends[0] = end;
        body.TraceLines(trace);
        return trace.Intersections[0];
    }

    private sealed class Standing(Vector3d feet) : IHumanBodyState
    {
        public Vector3d Feet => feet;

        public Vector3d Eyes => new(feet.X, feet.Y, feet.Z + (1.7 * Metre));

        public bool IsKeepingLow => false;

        public bool IsActive => true;
    }
}
