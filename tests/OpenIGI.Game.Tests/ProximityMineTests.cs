using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Project I.G.I.'s laid mine, tick by tick as <c>Mine_New</c> (0x476040), its run (0x475CC0), its proximity handler
/// (0x475DF0) and its exploding object (0x481970, 0x481E60, 0x481EA0) have it.
/// </summary>
public sealed class ProximityMineTests
{
    private const float HumanRadius = 2252.8f;

    private static readonly Vector3f Up = new(0f, 0f, 1f);

    // A mine on a floor, face up, with the shipped config: 2 s arming, 0.5 s countdown, 1.5 m sense, 3 m blast.
    private static PlacedMine FloorMine(bool remote = false) =>
        new(default, M203Physics.LaunchOrientation(Up), "136_01_1", remote: remote);

    // Each tick as the simulation runs it: every human's place, then the mine's run.
    private static (bool Triggered, bool Exploded) Step(PlacedMine mine, Vector3d? human)
    {
        bool triggered = human is { } at && mine.Sense(at, HumanRadius);
        return (triggered, mine.Run());
    }

    [Fact]
    public void ItsFrameTurnsItsFaceToTheSurfaceNormal()
    {
        Vector3f wall = new(0.6f, -0.8f, 0f);
        PlacedMine mine = new(default, M203Physics.LaunchOrientation(wall), "136_01_1");

        Assert.Equal(wall.X, mine.Face.X, 5);
        Assert.Equal(wall.Y, mine.Face.Y, 5);
        Assert.Equal(wall.Z, mine.Face.Z, 5);
    }

    [Fact]
    public void TheConfigSecondsRoundToTicksAtThirty()
    {
        PlacedMine mine = FloorMine();

        Assert.Equal(60, mine.Fuse.ArmTicks);
        Assert.Equal(15, mine.Fuse.CountdownTicks);
        Assert.Equal(3f * 4096f, mine.ExplosionRadiusUnits);
    }

    [Fact]
    public void NobodySetsItOffWhileItArms()
    {
        PlacedMine mine = FloorMine();
        Vector3d standingOnIt = new(0, 0, 3877.2);

        // Its run counts the delay down from the tick it is laid; a human on it the whole time does nothing.
        for (int tick = 0; tick < 60; tick++)
        {
            (bool triggered, bool exploded) = Step(mine, standingOnIt);
            Assert.False(triggered);
            Assert.False(exploded);
        }

        // Armed now: the next place told sets it off, once.
        Assert.True(Step(mine, standingOnIt).Triggered);
        Assert.False(Step(mine, standingOnIt).Triggered);
    }

    [Fact]
    public void ItGoesOffOnTheSeventeenthRunAfterTheTickItIsSetOff()
    {
        PlacedMine mine = FloorMine();
        for (int tick = 0; tick < 60; tick++)
        {
            Step(mine, null);
        }

        // The run that is set off counts 15 to 14; fifteen runs reach nought, the next raises the explode flag, and the
        // exploding object goes off on the run after that (0x481970 runs before the countdown in 0x475CC0).
        Assert.True(Step(mine, new Vector3d(0, 0, 4096)).Triggered);
        int runs = 1;
        while (!Step(mine, null).Exploded)
        {
            runs++;
            Assert.True(runs < 40);
        }

        Assert.Equal(17, runs + 1);
        Assert.True(mine.IsExploded);
    }

    [Fact]
    public void OnlyAHumanInFrontOfItsFaceAndWithinReachSetsItOff()
    {
        // 1.5 m of reach, and the human's own radius: the test is radius * 4096 > distance - human radius.
        float edge = (1.5f * 4096f) + HumanRadius;

        PlacedMine below = Armed(FloorMine());
        Assert.False(below.Sense(new Vector3d(0, 0, -100), HumanRadius));

        PlacedMine far = Armed(FloorMine());
        Assert.False(far.Sense(new Vector3d(0, 0, edge + 1), HumanRadius));

        PlacedMine near = Armed(FloorMine());
        Assert.True(near.Sense(new Vector3d(0, 0, edge - 1), HumanRadius));
    }

    [Fact]
    public void ARemoteMineIgnoresHumansAndGoesOffNineTicksAfterTheMapClick()
    {
        PlacedMine mine = Armed(FloorMine(remote: true));
        Assert.False(mine.Sense(new Vector3d(0, 0, 4096), HumanRadius));

        mine.Fuse.SetOffFromMap();
        int runs = 1;
        while (!mine.Run())
        {
            runs++;
            Assert.True(runs < 40);
        }

        // Nine runs to nought, one for the flag, one for the explosion.
        Assert.Equal(11, runs);
    }

    [Fact]
    public void ARoundOrABlastSetsItOffOnItsNextRunArmedOrNot()
    {
        PlacedMine shot = FloorMine();
        shot.Hit(1f);
        Assert.True(shot.Run());

        PlacedMine blasted = FloorMine();
        blasted.Blast(0.5f);
        Assert.False(blasted.Run());
        blasted.Blast(0.5f);
        Assert.True(blasted.Run());
    }

    /// <summary>
    /// Measured on retail (mission 1, a Glock at 0.2 a round): the damage store chops, so five rounds leave 0.99999994
    /// and the mine stands; the sixth sets it off on its next run.
    /// </summary>
    [Fact]
    public void FiveGlockRoundsLeaveItStandingAndTheSixthSetsItOff()
    {
        PlacedMine mine = FloorMine();
        for (int round = 0; round < 5; round++)
        {
            mine.Hit(0.2f);
            Assert.False(mine.Run());
        }

        mine.Hit(0.2f);
        Assert.True(mine.Run());
    }

    [Fact]
    public void TheSimulationLaysTicksAndReportsItsMines()
    {
        GrenadeSimulation simulation = new();
        PlacedMine mine = FloorMine();
        simulation.Lay(mine);
        Vector3d? human = null;
        simulation.MineSensors = () => human is { } at ? [new HumanPlace(at, HumanRadius)] : [];

        for (int tick = 0; tick < 60; tick++)
        {
            simulation.Tick(null);
        }

        human = new Vector3d(0, 0, 4096);
        simulation.Tick(null);
        Assert.Single(simulation.MineTriggers);

        int ticks = 1;
        while (simulation.MineBlasts.Count == 0)
        {
            simulation.Tick(null);
            ticks++;
            Assert.True(ticks < 40);
        }

        Assert.Equal(17, ticks);
        Assert.Same(mine, Assert.Single(simulation.MineBlasts));
        Assert.Empty(simulation.Mines);
    }

    private static PlacedMine Armed(PlacedMine mine)
    {
        for (int tick = 0; tick < 60; tick++)
        {
            mine.Run();
        }

        return mine;
    }
}
