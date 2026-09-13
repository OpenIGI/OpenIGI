using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What the crosshair is over. The bug this pins is that it has to be found by the same tests a round
/// would meet — a convergence that cannot see people sends every shot at a guard half a metre wide.
/// </summary>
public sealed class AimConvergenceTests
{
    private const double Metre = 4096.0;
    private const double Reach = 300.0 * Metre;

    private static readonly Vector3d Camera = new(0.0, 0.0, 1.8 * Metre);
    private static readonly Vector3f Forward = new(0f, 1f, 0f);

    /// <summary>A ray that meets nothing keeps its far end, which is no convergence at all.</summary>
    [Fact]
    public void AnEmptyRayReturnsItsFarEnd()
    {
        Vector3d point = AimConvergence.Resolve(Camera, Forward, Reach, null, null, null);

        Assert.Equal(Camera.Y + Reach, point.Y, 3);
    }

    /// <summary>A person in front of a wall is what the crosshair is over — not the wall.</summary>
    /// <remarks>
    /// The regression. The first attempt asked the renderer, whose query sees walls and terrain and
    /// looks straight through people, so a shot aimed at a guard converged on the hillside behind him.
    /// </remarks>
    [Fact]
    public void APersonInFrontOfAWallWinsTheWall()
    {
        Vector3d point = AimConvergence.Resolve(
            Camera,
            Forward,
            Reach,
            World(40.0),
            null,
            Human(12.0));

        Assert.Equal(Camera.Y + (12.0 * Metre), point.Y, 3);
    }

    /// <summary>And a wall in front of a person wins the person.</summary>
    [Fact]
    public void AWallInFrontOfAPersonWinsThePerson()
    {
        Vector3d point = AimConvergence.Resolve(
            Camera,
            Forward,
            Reach,
            World(6.0),
            null,
            Human(12.0));

        Assert.Equal(Camera.Y + (6.0 * Metre), point.Y, 3);
    }

    /// <summary>
    /// The ground counts, because the object index does not carry it and it is most of what is aimed at.
    /// </summary>
    [Fact]
    public void TerrainIsConsideredAlongsideTheObjects()
    {
        Vector3d point = AimConvergence.Resolve(
            Camera,
            Forward,
            Reach,
            World(40.0),
            World(9.0),
            null);

        Assert.Equal(Camera.Y + (9.0 * Metre), point.Y, 3);
    }

    /// <summary>Whichever of the three is nearest wins, and only that one.</summary>
    [Fact]
    public void TheNearestOfTheThreeWins()
    {
        Assert.Equal(
            Camera.Y + (3.0 * Metre),
            AimConvergence.Resolve(Camera, Forward, Reach, World(3.0), World(9.0), Human(12.0)).Y,
            3);

        Assert.Equal(
            Camera.Y + (2.0 * Metre),
            AimConvergence.Resolve(Camera, Forward, Reach, World(30.0), World(9.0), Human(2.0)).Y,
            3);
    }

    /// <summary>A source reporting nonsense is skipped rather than believed.</summary>
    /// <remarks>
    /// A trace that answers with an infinite or negative distance would otherwise beat every real
    /// contact and point the shot at a place nothing is.
    /// </remarks>
    [Fact]
    public void AContactWithNoUsableDistanceIsIgnored()
    {
        WorldTraceCast broken = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            hit = new WorldTraceHit(
                new Vector3d(999.0, 999.0, 999.0),
                default,
                double.NaN,
                0.0,
                0,
                WorldTraceHitKind.StaticObject);
            return true;
        };

        Vector3d point = AimConvergence.Resolve(Camera, Forward, Reach, broken, null, Human(12.0));

        Assert.Equal(Camera.Y + (12.0 * Metre), point.Y, 3);
    }

    private static WorldTraceCast World(double metresAhead) =>
        (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            Vector3d at = new(Camera.X, Camera.Y + (metresAhead * Metre), Camera.Z);
            hit = new WorldTraceHit(
                at,
                new Vector3f(0f, -1f, 0f),
                metresAhead * Metre,
                0.0,
                0,
                WorldTraceHitKind.StaticObject);
            return true;
        };

    private static HumanRayCast Human(double metresAhead) =>
        (Vector3d origin, Vector3d end, out HumanHit hit) =>
        {
            Vector3d at = new(Camera.X, Camera.Y + (metresAhead * Metre), Camera.Z);
            hit = new HumanHit(at, metresAhead * Metre, 12, 0f, 0f, 1);
            return true;
        };
}
