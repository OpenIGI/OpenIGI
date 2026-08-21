using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class M203PhysicsTests
{
    [Fact]
    public void LaunchOrientationMatchesRetailBasisForCardinalDirections()
    {
        Assert.Equal(Matrix3f.Identity, M203Physics.LaunchOrientation(Vector3f.UnitY));

        Matrix3f alongX = M203Physics.LaunchOrientation(Vector3f.UnitX);
        Assert.Equal(new Vector3f(0f, 1f, 0f), alongX.Row1);
        Assert.Equal(new Vector3f(-1f, 0f, 0f), alongX.Row2);
        Assert.Equal(new Vector3f(0f, 0f, 1f), alongX.Row3);

        Matrix3f alongZ = M203Physics.LaunchOrientation(Vector3f.UnitZ);
        Assert.Equal(new Vector3f(1f, 0f, 0f), alongZ.Row1);
        Assert.Equal(new Vector3f(0f, 0f, -1f), alongZ.Row2);
        Assert.Equal(new Vector3f(0f, 1f, 0f), alongZ.Row3);
    }

    [Fact]
    public void SpawnStoresLaunchBasisInsteadOfRebuildingItFromFallingVelocity()
    {
        M203Simulation simulation = new();
        Vector3f direction = new(0.26726124f, 0.8017837f, 0.5345225f);
        Matrix3f expected = M203Physics.LaunchOrientation(direction);

        simulation.Spawn(new M203Projectile(
            default,
            direction,
            85f,
            3f,
            1f));

        M203ProjectileState projectile = Assert.Single(simulation.Projectiles);
        Assert.Equal(expected, projectile.Orientation);

        simulation.Tick(walls: null, humans: null);

        projectile = Assert.Single(simulation.Projectiles);
        Assert.Equal(expected, projectile.Orientation);
        Assert.NotEqual(direction, projectile.Velocity);
    }

    [Fact]
    public void LaunchVelocityUsesTheNativeKmHToWorldTickConversion()
    {
        float expected = 85f * 4096f / 3.6f / 30f;
        Vector3f velocity = M203Physics.LaunchVelocity(new Vector3f(0f, 1f, 0f), 85f);

        Assert.Equal(expected, velocity.Y, 0.001f);
        Assert.Equal(0f, velocity.X);
        Assert.Equal(0f, velocity.Z);
    }

    [Fact]
    public void M203FallbackConfigurationUsesTheRetailWeaponConfig()
    {
        Assert.Equal(270f, M203Physics.DefaultMuzzleVelocityKilometresPerHour);
        Assert.Equal(5f, M203Physics.DefaultExplosionRadiusMetres);
        Assert.Equal(1f, M203Physics.DefaultActivateRadiusMetres);
    }

    [Fact]
    public void TickAppliesNativeDragAndGravityBeforeMoving()
    {
        M203Simulation simulation = new();
        simulation.Spawn(new M203Projectile(
            default,
            new Vector3f(0f, 1f, 0f),
            85f,
            3f,
            1f));

        simulation.Tick(walls: null, humans: null);

        M203ProjectileState projectile = Assert.Single(simulation.Projectiles);
        float launchSpeed = 85f * M203Physics.KilometresPerHourToUnitsPerTick;
        Assert.Equal(launchSpeed * M203Physics.Drag, projectile.Velocity.Y, 0.01f);
        Assert.Equal(-M203Physics.GravityPerTick * M203Physics.Drag, projectile.Velocity.Z, 0.01f);
        Assert.Equal(projectile.Velocity.Y, projectile.Position.Y, 0.01f);
        Assert.Equal(projectile.Velocity.Z, projectile.Position.Z, 0.01f);
    }

    [Fact]
    public void ImpactEmitsTheConfiguredM203ExplosionAndRemovesTheRound()
    {
        M203Simulation simulation = new();
        simulation.Spawn(new M203Projectile(
            default,
            new Vector3f(0f, 1f, 0f),
            85f,
            3f,
            1f));

        static bool Wall(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            if (end.Y > origin.Y)
            {
                hit = new WorldRayHit(end, new Vector3f(0f, -1f, 0f), 1f);
                return true;
            }

            hit = default;
            return false;
        }

        simulation.Tick(Wall, humans: null);

        M203Detonation detonation = Assert.Single(simulation.Detonations);
        Assert.Equal(3f * 4096f, detonation.RadiusUnits);
        Assert.Equal(0f, detonation.FalloffUnits);
        Assert.Equal(1f * 4096f, detonation.ActivateRadiusUnits);
        Assert.Empty(simulation.Projectiles);
    }

    [Fact]
    public void MissedRoundsExpireAndFreeAnM203LiveSlot()
    {
        M203Simulation simulation = new();
        for (int i = 0; i < M203Simulation.MaximumLive; i++)
        {
            Assert.True(simulation.Spawn(new M203Projectile(
                default,
                new Vector3f(0f, 1f, 0f),
                85f,
                3f,
                1f)));
        }

        Assert.False(simulation.Spawn(new M203Projectile(
            default,
            new Vector3f(0f, 1f, 0f),
            85f,
            3f,
            1f)));

        for (int tick = 0; tick < M203Simulation.MaximumLifetimeTicks; tick++)
        {
            simulation.Tick(walls: null, humans: null);
        }

        Assert.Empty(simulation.Projectiles);
        Assert.True(simulation.Spawn(new M203Projectile(
            default,
            new Vector3f(0f, 1f, 0f),
            85f,
            3f,
            1f)));
    }

}
