using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class MissileSimulationTests
{
    [Fact]
    public void LauncherMissileUsesAuthoredAntiTankGuidanceAndLaunchSpeed()
    {
        MissileSimulation simulation = new();
        Assert.True(simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(40960, 0, 0),
            MissileType.MissileAntiTank,
            muzzleVelocityMetresPerSecond: 5f));

        LiveMissile missile = Assert.Single(simulation.Missiles);
        float speed = MathF.Sqrt(
            (missile.Velocity.X * missile.Velocity.X)
            + (missile.Velocity.Y * missile.Velocity.Y)
            + (missile.Velocity.Z * missile.Velocity.Z));

        // A rocket leaves the tube at the config's launch speed, not the dummy 5 m/s the weapon
        // table carries to keep carriers out of g_ballistics; thrust builds it up from there.
        float expected = MissileConfig.AntiTank.LaunchMetresPerSecond
            * MissileFlight.UnitsPerMetre / MissileFlight.TicksPerSecond;
        Assert.Equal(expected, speed, 0.5f);
        Assert.True(missile.Velocity.Z > 0f);
    }

    [Fact]
    public void ADirectRocketAcceleratesUnderThrustTowardCruise()
    {
        // 0x00428D00 ramps a throttle and drives the body forward; a LAW leaves slow and speeds up.
        MissileSimulation simulation = new();
        Assert.True(simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(4_096_000, 0, 0),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 5f));

        static float Speed(LiveMissile m) => MathF.Sqrt(
            (m.Velocity.X * m.Velocity.X) + (m.Velocity.Y * m.Velocity.Y) + (m.Velocity.Z * m.Velocity.Z));

        float launch = Speed(simulation.Missiles[0]);
        for (int i = 0; i < 60; i++)
        {
            simulation.Tick(walls: null, humans: null);
        }

        float cruising = Speed(simulation.Missiles[0]);
        float cruiseUnitsPerTick = MissileConfig.Direct.CruiseMetresPerSecond
            * MissileFlight.UnitsPerMetre / MissileFlight.TicksPerSecond;

        Assert.True(cruising > launch * 1.5f, $"expected acceleration: {launch} -> {cruising}");
        Assert.True(cruising <= cruiseUnitsPerTick + 1f, $"overshot cruise: {cruising}");
    }

    [Fact]
    public void LauncherMissileDetonatesAtItsFirstWorldImpact()
    {
        MissileSimulation simulation = new();
        simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(40960, 0, 0),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 5f);

        static bool Wall(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            if (origin.X < 5000d && end.X >= 5000d)
            {
                hit = new WorldRayHit(new Vector3d(5000d, 0d, 0d), Vector3f.UnitX, end.X - 5000d);
                return true;
            }

            hit = default;
            return false;
        }

        for (int tick = 0; tick < 20 && simulation.Detonations.Count == 0; tick++)
        {
            simulation.Tick(Wall, humans: null);
        }

        MissileDetonation detonation = Assert.Single(simulation.Detonations);
        Assert.Equal(5000d, detonation.Position.X);
        Assert.Equal(MissileExplosion.DamageFactor, detonation.DamageFactor);
        Assert.Empty(simulation.Missiles);
    }

    [Fact]
    public void HumanIntersectionDoesNotApplyGunDamage()
    {
        MissileSimulation simulation = new();
        simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(40960, 0, 0),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 5f);

        int probes = 0;
        bool Probe(Vector3d origin, Vector3d end, out HumanHit hit)
        {
            probes++;
            hit = new HumanHit(new Vector3d(1000d, 0d, 0d), 1000d, 0, 0f, 0f);
            return true;
        }

        simulation.Tick(walls: null, Probe);

        Assert.Equal(1, probes);
        MissileDetonation detonation = Assert.Single(simulation.Detonations);
        Assert.Equal(1000d, detonation.Position.X);
        Assert.Equal(MissileExplosion.DamageFactor, detonation.DamageFactor);
        Assert.Empty(simulation.Missiles);
    }

    [Fact]
    public void TheMotorBurnsOutAndTheRocketCoasts()
    {
        // a1+2148 counts the burn down (0x0042939A); at zero the body coasts on drag alone.
        MissileConfig config = MissileConfig.Direct with { MotorBurnTicks = 5, CruiseMetresPerSecond = 90f };
        MissileSimulation simulation = new();
        Assert.True(simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(40_960_000, 0, 0),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 5f,
            config: config));

        static float Speed(LiveMissile m) => MathF.Sqrt(
            (m.Velocity.X * m.Velocity.X) + (m.Velocity.Y * m.Velocity.Y) + (m.Velocity.Z * m.Velocity.Z));

        for (int i = 0; i < 5; i++)
        {
            simulation.Tick(walls: null, humans: null);
        }

        Assert.False(simulation.Missiles[0].Powered);
        float atBurnout = Speed(simulation.Missiles[0]);
        simulation.Tick(walls: null, humans: null);
        Assert.True(Speed(simulation.Missiles[0]) < atBurnout, "a coasting rocket loses speed to drag");
    }

    [Fact]
    public void ABombFallsUnderGravity()
    {
        MissileSimulation simulation = new();
        Assert.True(simulation.Spawn(
            new Vector3d(0, 0, 40960),
            new Vector3d(0, 0, 40960),
            MissileType.Bomb,
            muzzleVelocityMetresPerSecond: 5f,
            config: MissileConfig.Bomb));

        for (int i = 0; i < 5; i++)
        {
            simulation.Tick(walls: null, humans: null);
        }

        LiveMissile bomb = Assert.Single(simulation.Missiles);
        Assert.True(bomb.Velocity.Z < 0f, "a bomb accelerates downward");
        Assert.True(bomb.Position.Z < 40960d, "a bomb loses height");
    }

    [Fact]
    public void ADirectMissileDoesNotFallUnderGrenadeGravity()
    {
        // 0x00428910 clears both steer rates; 0x00428D00 drives thrust, never grenade gravity.
        MissileSimulation simulation = new();
        Assert.True(simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(409600, 0, 0),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 30f));

        double z0 = simulation.Missiles[0].Position.Z;
        for (int i = 0; i < 30; i++)
        {
            simulation.Tick(walls: null, humans: null);
        }

        LiveMissile missile = Assert.Single(simulation.Missiles);
        Assert.Equal(z0, missile.Position.Z, 0.01);
        Assert.Equal(0f, missile.Velocity.Z, 0.01);
    }

    [Fact]
    public void MissedMissilesExpireAndFreeALauncherLiveSlot()
    {
        MissileSimulation simulation = new();
        for (int i = 0; i < MissileSimulation.MaximumLive; i++)
        {
            Assert.True(simulation.Spawn(
                Vector3d.Zero,
                new Vector3d(409600, 0, 0),
                MissileType.MissileDirect,
                muzzleVelocityMetresPerSecond: 5f));
        }

        Assert.False(simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(409600, 0, 0),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 5f));

        for (int tick = 0; tick < MissileSimulation.MaximumLifetimeTicks; tick++)
        {
            simulation.Tick(walls: null, humans: null);
        }

        Assert.Empty(simulation.Missiles);
        Assert.True(simulation.Spawn(
            Vector3d.Zero,
            new Vector3d(409600, 0, 0),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 5f));
    }

}
