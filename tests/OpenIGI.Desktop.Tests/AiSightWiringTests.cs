using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Desktop.Tests;

/// <summary>Locks the AI's sight request to physical world geometry and retail's contact rule.</summary>
public sealed class AiSightWiringTests
{
    private static readonly Vector3d Eye = Vector3d.Zero;
    private static readonly Vector3d Mark = new(0.0, 10_000.0, 0.0);

    [Fact]
    public void AiSightDoesNotTurnAuthoredDoorwayAndWindowPortalsIntoSolidWalls()
    {
        WorldTraceRequest captured = default;
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            captured = request;
            hit = default;
            return false;
        };

        Assert.True(SoldierSimulation.HasLineOfSight(trace, Eye, Mark, Igi1HumanAi.MinimumSightVisibility));

        Assert.Equal(WorldTraceMask.World, captured.Mask);
        Assert.False(captured.TracePortals);
    }

    [Theory]
    [InlineData(WorldTraceHitKind.StaticObject, 8_200.0, false)]
    [InlineData(WorldTraceHitKind.StaticObject, 7_000.0, false)]
    [InlineData(WorldTraceHitKind.Terrain, 9_500.0, false)]
    [InlineData(WorldTraceHitKind.DynamicObject, 9_900.0, false)]
    [InlineData(WorldTraceHitKind.DynamicObject, 8_000.0, false)]
    [InlineData(WorldTraceHitKind.DynamicObject, 7_000.0, false)]
    public void SolidGeometryHidesTheTargetWhereverItIsMet(WorldTraceHitKind kind, double distance, bool seen)
    {
        // The 0.79 that 0x489B20 compares is a visibility -- what the materials along the line let through -- and not
        // a place along it: a solid surface leaves nothing wherever it stands (0x4C35F0), which is what the probe
        // measured as 0.0 on every blocked cast, a static roof met 82% along among them. A moving object is no
        // different: a door leaf a pace in front of the target hides it, where a tolerance by distance let a guard
        // see, shout at and fire on a player standing at a closed door.
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            hit = new WorldTraceHit(default, default, distance, distance / 10_000.0, Material: 3, kind);
            return true;
        };

        Assert.Equal(seen, SoldierSimulation.HasLineOfSight(trace, Eye, Mark, Igi1HumanAi.MinimumSightVisibility));
    }

    [Theory]
    [InlineData(14, 3, true)]
    [InlineData(18, 1, true)]
    [InlineData(18, 2, false)]
    public void WhatIsSeenThroughTakesItsMaterialsShare(int material, int surfaces, bool seen)
    {
        // DefineQMaterial's first argument: Glass lets the whole line through, Fence 0.8 of it. A soldier wants 0.79
        // left, so one run of chain-link is seen through and two (0.64) are not.
        Vector3f facingTheEye = new(0f, -1f, 0f);
        int met = 0;
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            bool found = met < surfaces;
            hit = found
                ? new WorldTraceHit(default, facingTheEye, 1_000.0, 0.1, material, WorldTraceHitKind.StaticObject)
                : default;
            met++;
            return found;
        };

        Assert.Equal(seen, SoldierSimulation.HasLineOfSight(trace, Eye, Mark, Igi1HumanAi.MinimumSightVisibility));
    }

    [Fact]
    public void AGunStandsAsideOnlyWhenItSaysSo()
    {
        // 0x4752B0: the stand right in front of a gunner's eye is no cover between him and the one he looks at, but the
        // same stand hides anyone behind it from a pair it has no part in.
        GameLoop loop = new(new ManualTimeSource());
        OpenIGI.Game.Flow.Igi1LevelTaskTypes.Register(loop.Types);
        OpenIGI.Game.Igi1.Igi1TaskTypes.Register(loop.Types);
        GameTask gun = loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        WorldTraceCast trace = (in WorldTraceRequest request, out WorldTraceHit hit) =>
        {
            bool atStand = request.Origin.Y < 500.0;
            hit = atStand ? new WorldTraceHit(default, default, 400.0, 0.04, Material: 0, WorldTraceHitKind.StaticObject, gun) : default;
            return atStand;
        };

        Assert.False(SoldierSimulation.HasLineOfSight(trace, Eye, Mark, Igi1HumanAi.MinimumSightVisibility));
        Assert.False(SoldierSimulation.HasLineOfSight(trace, Eye, Mark, Igi1HumanAi.MinimumSightVisibility, standsAside: _ => false));
        Assert.True(SoldierSimulation.HasLineOfSight(
            trace, Eye, Mark, Igi1HumanAi.MinimumSightVisibility, standsAside: owner => ReferenceEquals(owner, gun)));
    }
}
